using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using FFmpeg.AutoGen;
using Mp4BoxKit;
using Mp4BoxKit.FFmpeg.Tests;

unsafe class Program
{
    static void Check(int code)
    {
        if (code < 0)
        {
            throw new InvalidOperationException($"FFmpeg error {code}");
        }
    }

    static void Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var seconds = args.Length > 0 ? int.Parse(args[0]) : 60;
        var path = args.Length > 1 ? args[1] : "live-probe.mp4";
        var realtime = args.Contains("--realtime");
        var continuousAudio = args.Contains("--continuous-audio");
        var lateAudio = args.Contains("--late-audio");

        Directory.CreateDirectory(
            Path.GetDirectoryName(Path.GetFullPath(path))!);
        FFmpegLoader.Initialize();
        ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);

        using var log = new StreamWriter(path + ".csv")
        {
            AutoFlush = true
        };
        log.WriteLine(
            "media_seconds,wall_seconds,managed_bytes,working_set," +
            "allocated_bytes,segments,bytes");

        using var video = new Encoder(true);
        using var audio = new Encoder(false);
        using var file = File.Create(path);
        var watch = Stopwatch.StartNew();
        long bytes = 0;
        long segments = 0;
        long videoPackets = 0;
        long audioPackets = 0;

        using var writer = new Mp4FragmentedWriter(
            new StreamMp4Output(file),
            new Mp4FragmentedWriterOptions
            {
                FragmentBoundary = Fmp4FragmentBoundary.EveryKeyFrame
            },
            part =>
            {
                bytes += part.Data.Length;
                if (part.Kind == Fmp4SegmentKind.Media)
                {
                    segments++;
                    if (segments % 30 == 0)
                    {
                        Thread.Sleep(20);
                    }
                }
            });

        // Both streams are shifted by 2 video ticks (80 ms), preserving A/V alignment.
        var vt = writer.AddVideo(new VideoTrackOptions(
            Codec.H264,
            640,
            360,
            25,
            AvcC(video.Extra)));
        var at = writer.AddAudio(new AudioTrackOptions(
            Codec.Aac,
            1,
            48_000,
            CodecConfiguration: Esds(audio.Extra)));

        long lastV = -1;
        long lastA = -1;

        void Drain(Encoder encoder, bool isVideo)
        {
            while (encoder.Receive())
            {
                var p = encoder.Packet;
                var dts = p->dts + (isVideo ? 2 : 3840);
                var pts = p->pts + (isVideo ? 2 : 3840);
                if (isVideo)
                {
                    if (dts <= lastV)
                    {
                        throw new InvalidDataException("Video DTS regression");
                    }

                    lastV = dts;
                    videoPackets++;
                }
                else
                {
                    // Late audio, then 10-second outages every minute. No silence synthesis.
                    var time = (double)p->pts / 48_000;
                    if (!continuousAudio &&
                        (time < 5 || (!lateAudio && time % 60 is >= 30 and < 40)))
                    {
                        ffmpeg.av_packet_unref(p);
                        continue;
                    }

                    if (dts <= lastA)
                    {
                        throw new InvalidDataException("Audio DTS regression");
                    }

                    lastA = dts;
                    audioPackets++;
                }

                writer.WriteSample(
                    isVideo ? vt : at,
                    new ReadOnlySpan<byte>(p->data, p->size),
                    pts,
                    dts,
                    (p->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0
                        ? SampleFlags.KeyFrame
                        : SampleFlags.None,
                    duration: isVideo ? 1 : 1024);
                ffmpeg.av_packet_unref(p);
            }
        }

        long audioPts = 0;
        for (var i = 0; i < checked(seconds * 25); i++)
        {
            if (realtime)
            {
                var delay = i / 25.0 - watch.Elapsed.TotalSeconds;
                if (delay > 0)
                {
                    Thread.Sleep(TimeSpan.FromSeconds(delay));
                }
            }

            video.Send(i);
            Drain(video, true);
            while (audioPts * 25 <= (long)i * 48_000)
            {
                audio.Send(audioPts);
                audioPts += 1024;
                Drain(audio, false);
            }

            if (i % 250 == 0)
            {
                var line =
                    $"{i / 25}," +
                    $"{watch.Elapsed.TotalSeconds:F3}," +
                    $"{GC.GetTotalMemory(false)}," +
                    $"{Environment.WorkingSet}," +
                    $"{GC.GetTotalAllocatedBytes(false)}," +
                    $"{segments}," +
                    $"{bytes}";
                log.WriteLine(line);
                Console.WriteLine(line);
            }
        }

        video.Flush();
        Drain(video, true);
        audio.Flush();
        Drain(audio, false);
        writer.Complete();
        file.Flush();
        Console.WriteLine(
            $"COMPLETE media={seconds}s wall={watch.Elapsed} " +
            $"videoPackets={videoPackets} audioPackets={audioPackets} " +
            $"bytes={bytes}");

        // Close the output handle before independent native demux/decode.
        file.Dispose();
        Validate(path, videoPackets, audioPackets);
        InspectFragments(path);
    }

    static void InspectFragments(string path)
    {
        using var input = File.OpenRead(path);
        using var report = new StreamWriter(path + ".fragments.csv");
        report.WriteLine("fragment,track,first_dts,last_end,samples");

        var header = new byte[8];
        var fragment = 0;
        var ends = new Dictionary<uint, long>();
        while (input.Position < input.Length)
        {
            input.ReadExactly(header);
            var size = BinaryPrimitives.ReadUInt32BigEndian(header);
            var type = System.Text.Encoding.ASCII.GetString(header, 4, 4);
            if (size < 8)
            {
                throw new InvalidDataException("Unsupported probe box size");
            }

            if (type != "moof")
            {
                input.Seek(size - 8, SeekOrigin.Current);
                continue;
            }

            var payload = new byte[checked((int)size - 8)];
            input.ReadExactly(payload);
            fragment++;
            foreach (var traf in Children(payload, "traf"))
            {
                var tfhd = Children(traf, "tfhd").Single();
                var track = BinaryPrimitives.ReadUInt32BigEndian(tfhd.AsSpan(4));
                var tfdt = Children(traf, "tfdt").Single();
                var start = checked((long)(tfdt[0] == 1
                    ? BinaryPrimitives.ReadUInt64BigEndian(tfdt.AsSpan(4))
                    : BinaryPrimitives.ReadUInt32BigEndian(tfdt.AsSpan(4))));
                var end = start;
                var count = 0;
                foreach (var trun in Children(traf, "trun"))
                {
                    var flags =
                        BinaryPrimitives.ReadUInt32BigEndian(trun) & 0xffffff;
                    var n = BinaryPrimitives.ReadUInt32BigEndian(trun.AsSpan(4));
                    var cursor =
                        8 +
                        ((flags & 1) != 0 ? 4 : 0) +
                        ((flags & 4) != 0 ? 4 : 0);
                    for (var i = 0; i < n; i++)
                    {
                        if ((flags & 0x100) == 0)
                        {
                            throw new InvalidDataException(
                                "Probe expects explicit trun duration");
                        }

                        var duration = BinaryPrimitives.ReadUInt32BigEndian(
                            trun.AsSpan(cursor));
                        cursor += 4;
                        if (track == 2 && duration != 1024)
                        {
                            throw new InvalidDataException(
                                "Unexpected AAC duration");
                        }

                        end = checked(end + duration);
                        count++;
                        if ((flags & 0x200) != 0)
                        {
                            cursor += 4;
                        }

                        if ((flags & 0x400) != 0)
                        {
                            cursor += 4;
                        }

                        if ((flags & 0x800) != 0)
                        {
                            cursor += 4;
                        }
                    }
                }

                if (ends.TryGetValue(track, out var previous) &&
                    start < previous)
                {
                    throw new InvalidDataException(
                        "Overlapping fragment decode timelines");
                }

                ends[track] = end;
                report.WriteLine($"{fragment},{track},{start},{end},{count}");
            }
        }

        Console.WriteLine(
            $"FRAGMENT_TIMELINES_OK fragments={fragment}");
    }

    static List<byte[]> Children(byte[] data, string type)
    {
        var result = new List<byte[]>();
        for (var cursor = 0; cursor < data.Length;)
        {
            var size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
                data.AsSpan(cursor)));
            if (size < 8 || size > data.Length - cursor)
            {
                throw new InvalidDataException("Malformed probe box");
            }

            if (System.Text.Encoding.ASCII.GetString(
                    data, cursor + 4, 4) == type)
            {
                result.Add(data.AsSpan(cursor + 8, size - 8).ToArray());
            }

            cursor += size;
        }

        return result;
    }

    static void Validate(string path, long expectedVideo, long expectedAudio)
    {
        AVFormatContext* format = null;
        Check(ffmpeg.avformat_open_input(&format, path, null, null));
        var decoders = new nint[2];
        var indexes = new int[2];
        var packets = new long[2];
        var frames = new long[2];
        var audioSamples = 0L;
        var lastDts = new long?[2];
        var packet = ffmpeg.av_packet_alloc();
        var frame = ffmpeg.av_frame_alloc();
        try
        {
            Check(ffmpeg.avformat_find_stream_info(format, null));
            for (var k = 0; k < 2; k++)
            {
                indexes[k] = ffmpeg.av_find_best_stream(
                    format,
                    k == 0
                        ? AVMediaType.AVMEDIA_TYPE_VIDEO
                        : AVMediaType.AVMEDIA_TYPE_AUDIO,
                    -1,
                    -1,
                    null,
                    0);
                Check(indexes[k]);
                var parameters = format->streams[indexes[k]]->codecpar;
                var codec = ffmpeg.avcodec_find_decoder(parameters->codec_id);
                var context = ffmpeg.avcodec_alloc_context3(codec);
                decoders[k] = (nint)context;
                Check(ffmpeg.avcodec_parameters_to_context(
                    context, parameters));
                Check(ffmpeg.avcodec_open2(context, codec, null));
            }

            void Receive(int k)
            {
                while (true)
                {
                    var code = ffmpeg.avcodec_receive_frame(
                        (AVCodecContext*)decoders[k], frame);
                    if (code == ffmpeg.AVERROR(ffmpeg.EAGAIN) ||
                        code == ffmpeg.AVERROR_EOF)
                    {
                        return;
                    }

                    Check(code);
                    frames[k]++;
                    if (k == 1)
                    {
                        audioSamples += frame->nb_samples;
                    }

                    ffmpeg.av_frame_unref(frame);
                }
            }

            while (true)
            {
                var code = ffmpeg.av_read_frame(format, packet);
                if (code == ffmpeg.AVERROR_EOF)
                {
                    break;
                }

                Check(code);
                var k = Array.IndexOf(indexes, packet->stream_index);
                if (k >= 0)
                {
                    if (lastDts[k] is { } last && packet->dts <= last)
                    {
                        throw new InvalidDataException(
                            "Native demux DTS regression");
                    }

                    lastDts[k] = packet->dts;
                    packets[k]++;
                    Check(ffmpeg.avcodec_send_packet(
                        (AVCodecContext*)decoders[k], packet));
                    Receive(k);
                }

                ffmpeg.av_packet_unref(packet);
            }

            for (var k = 0; k < 2; k++)
            {
                Check(ffmpeg.avcodec_send_packet(
                    (AVCodecContext*)decoders[k], null));
                Receive(k);
            }

            if (packets[0] != expectedVideo ||
                packets[1] != expectedAudio ||
                frames[0] != expectedVideo ||
                audioSamples != expectedAudio * 1024)
            {
                throw new InvalidDataException(
                    $"Native validation counts differ: " +
                    $"packets={string.Join(',', packets)} " +
                    $"frames={string.Join(',', frames)} " +
                    $"audioSamples={audioSamples}");
            }

            Console.WriteLine(
                $"NATIVE_DECODE_OK videoFrames={frames[0]} " +
                $"audioSamples={audioSamples}");
        }
        finally
        {
            foreach (var pointer in decoders)
            {
                var context = (AVCodecContext*)pointer;
                ffmpeg.avcodec_free_context(&context);
            }

            ffmpeg.av_packet_free(&packet);
            var releasedFrame = frame;
            ffmpeg.av_frame_free(&releasedFrame);
            ffmpeg.avformat_close_input(&format);
        }
    }

    sealed class Encoder : IDisposable
    {
        AVCodecContext* context;
        AVFrame* frame;
        public AVPacket* Packet;
        readonly bool video;

        public byte[] Extra => new ReadOnlySpan<byte>(
            context->extradata, context->extradata_size).ToArray();

        public Encoder(bool video)
        {
            this.video = video;
            var codec = ffmpeg.avcodec_find_encoder_by_name(
                video ? "libx264" : "aac");
            if (codec == null)
            {
                throw new InvalidOperationException("Encoder unavailable");
            }

            context = ffmpeg.avcodec_alloc_context3(codec);
            frame = ffmpeg.av_frame_alloc();
            Packet = ffmpeg.av_packet_alloc();
            if (context == null || frame == null || Packet == null)
            {
                throw new OutOfMemoryException();
            }

            context->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
            if (video)
            {
                context->width = 640;
                context->height = 360;
                context->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
                context->time_base = new AVRational { num = 1, den = 25 };
                context->framerate = new AVRational { num = 25, den = 1 };
                context->gop_size = 50;
                context->max_b_frames = 2;
                Check(ffmpeg.av_opt_set(
                    context->priv_data, "preset", "medium", 0));
                Check(ffmpeg.av_opt_set(
                    context->priv_data, "profile", "main", 0));
                Check(ffmpeg.av_opt_set(
                    context->priv_data, "crf", "18", 0));
                Check(ffmpeg.av_opt_set(
                    context->priv_data,
                    "x264-params",
                    "annexb=0:bframes=2:b-adapt=0:scenecut=0:threads=2",
                    0));
            }
            else
            {
                context->sample_rate = 48_000;
                context->sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_FLTP;
                context->time_base = new AVRational { num = 1, den = 48_000 };
                context->bit_rate = 96_000;
                ffmpeg.av_channel_layout_default(&context->ch_layout, 1);
            }

            Check(ffmpeg.avcodec_open2(context, codec, null));
            frame->format = video
                ? (int)context->pix_fmt
                : (int)context->sample_fmt;
            frame->width = context->width;
            frame->height = context->height;
            if (!video)
            {
                frame->nb_samples = context->frame_size;
                frame->sample_rate = 48_000;
                ffmpeg.av_channel_layout_copy(
                    &frame->ch_layout, &context->ch_layout);
            }

            Check(ffmpeg.av_frame_get_buffer(frame, 32));
        }

        public void Send(long pts)
        {
            Check(ffmpeg.av_frame_make_writable(frame));
            frame->pts = pts;
            if (video)
            {
                for (uint plane = 0; plane < 3; plane++)
                {
                    var width = plane == 0 ? 640 : 320;
                    var height = plane == 0 ? 360 : 180;
                    for (var y = 0; y < height; y++)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            frame->data[plane][
                                y * frame->linesize[plane] + x] =
                                plane == 0
                                    ? (byte)(16 + ((x + y + pts * 3) % 220))
                                    : (byte)128;
                        }
                    }
                }
            }
            else
            {
                for (var j = 0; j < frame->nb_samples; j++)
                {
                    ((float*)frame->data[0])[j] = (float)(
                        0.2 *
                        Math.Sin(2 * Math.PI * 440 * (pts + j) / 48_000));
                }
            }

            Check(ffmpeg.avcodec_send_frame(context, frame));
        }

        public void Flush()
            => Check(ffmpeg.avcodec_send_frame(context, null));

        public bool Receive()
        {
            var result = ffmpeg.avcodec_receive_packet(context, Packet);
            if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN) ||
                result == ffmpeg.AVERROR_EOF)
            {
                return false;
            }

            Check(result);
            return true;
        }

        public void Dispose()
        {
            var p = Packet;
            ffmpeg.av_packet_free(&p);
            Packet = null;

            var f = frame;
            ffmpeg.av_frame_free(&f);
            frame = null;

            var c = context;
            ffmpeg.avcodec_free_context(&c);
            context = null;
        }
    }

    static byte[] AvcC(ReadOnlySpan<byte> extra)
    {
        if (extra[0] == 1)
        {
            return extra.ToArray();
        }

        var nals = new List<byte[]>();
        while (extra.Length >= 4)
        {
            var size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(extra));
            nals.Add(extra.Slice(4, size).ToArray());
            extra = extra[(4 + size)..];
        }

        var sps = nals.Single(n => (n[0] & 31) == 7);
        var pps = nals.Single(n => (n[0] & 31) == 8);
        using var result = new MemoryStream();
        result.Write([1, sps[1], sps[2], sps[3], 255, 225]);

        void Nal(byte[] nal)
        {
            result.WriteByte((byte)(nal.Length >> 8));
            result.WriteByte((byte)nal.Length);
            result.Write(nal);
        }

        Nal(sps);
        result.WriteByte(1);
        Nal(pps);
        return result.ToArray();
    }

    static byte[] Esds(byte[] extra)
    {
        byte[] Descriptor(byte tag, byte[] payload)
            => [tag, checked((byte)payload.Length), .. payload];

        return
        [
            0,
            0,
            0,
            0,
            .. Descriptor(
                3,
                [
                    0,
                    1,
                    0,
                    .. Descriptor(
                        4,
                        [
                            0x40,
                            0x15,
                            0,
                            0,
                            0,
                            0,
                            1,
                            0x77,
                            0,
                            0,
                            1,
                            0x77,
                            0,
                            .. Descriptor(5, extra)
                        ]),
                    .. Descriptor(6, [2])
                ])
        ];
    }
}
