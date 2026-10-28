using FFmpeg.AutoGen;
using Xunit;

namespace Mp4BoxKit.FFmpeg.Tests;

public class NativeEncoderTests
{
    private static readonly object NativeLock = new();

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task MpegVideo_RoundTrips(bool fragmented, bool mpeg2)
    {
        var codec = mpeg2 ? Codec.Mpeg2Video : Codec.Mpeg4Video;
        var result = EncodeAndWrite(fragmented, videoCodec: codec);
        using var input = new MemoryStream(result.File);
        var reader = new Mp4Reader(input);
        await reader.ReadContainerInfoAsync();
        Assert.Equal(codec, Assert.Single(reader.Info.Tracks).Codec);
        foreach (var expected in result.Packets)
        {
            var sample = await reader.ReadSampleAsync();
            Assert.NotNull(sample);
            Assert.Equal(expected.Data, sample.Data.ToArray());
            Assert.Equal(expected.Pts, sample.Pts);
            Assert.Equal(expected.Dts, sample.Dts);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task H264_AvPackets_AreWrittenAndRead(bool fragmented)
        => await AssertRoundTrip(fragmented, false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task H265_AvPackets_AreWrittenAndRead(bool fragmented)
        => await AssertRoundTrip(fragmented, true);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Vp8AndVp9_AvPackets_AreWrittenAndRead(bool fragmented, bool vp9)
    {
        var (file, expected) = EncodeAndWrite(fragmented, videoCodec: vp9 ? Codec.Vp9 : Codec.Vp8);
        using var input = new MemoryStream(file);
        var reader = new Mp4Reader(input);
        await reader.ReadContainerInfoAsync();
        Assert.Equal(vp9 ? Codec.Vp9 : Codec.Vp8, Assert.Single(reader.Info.Tracks).Codec);
        foreach (var item in expected)
        {
            var sample = await reader.ReadSampleAsync();
            Assert.NotNull(sample);
            Assert.Equal(item.Data, sample.Data.ToArray());
            Assert.Equal(item.Pts, sample.Pts);
            Assert.Equal(item.Dts, sample.Dts);
        }
        Assert.Null(await reader.ReadSampleAsync());
    }

    private static async Task AssertRoundTrip(bool fragmented, bool hevc)
    {
        var (file, expected) = EncodeAndWrite(fragmented, hevc);
        using var input = new MemoryStream(file);
        var reader = new Mp4Reader(input);
        foreach (var item in expected)
        {
            var sample = await reader.ReadSampleAsync();
            Assert.NotNull(sample);
            Assert.Equal(item.Data, sample.Data.ToArray());
            Assert.Equal(item.Pts, sample.Pts);
            Assert.Equal(item.Dts, sample.Dts);
        }
        Assert.Null(await reader.ReadSampleAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task BFrames_PreserveDecodeAndPresentationOrder(bool fragmented, bool hevc)
    {
        var (file, expected) = EncodeAndWrite(fragmented, hevc, bFrames: true);
        Assert.Contains(expected, p => p.Pts != p.Dts);
        Assert.Contains(expected.Zip(expected.Skip(1)), p => p.First.Pts > p.Second.Pts);
        Assert.Equal(0, expected[0].Dts);
        using var input = new MemoryStream(file);
        var reader = new Mp4Reader(input);
        foreach (var item in expected)
        {
            var sample = await reader.ReadSampleAsync();
            Assert.NotNull(sample);
            Assert.Equal(item.Data, sample.Data.ToArray());
            Assert.Equal(item.Pts, sample.Pts);
            Assert.Equal(item.Dts, sample.Dts);
        }
        Assert.Null(await reader.ReadSampleAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SaveH264BFramesPreview(bool fragmented)
    {
        var (file, packets) = EncodeAndWrite(fragmented, bFrames: true);
        Assert.Contains(packets.Zip(packets.Skip(1)), p => p.First.Pts > p.Second.Pts);
        var directory = Environment.GetEnvironmentVariable("MP4BOXKIT_ARTIFACTS_DIR")
            ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts"));
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory,
            fragmented ? "h264-bframes-fragmented.mp4" : "h264-bframes-classic.mp4"), file);
    }

    [Fact]
    public void FastStartH264BFrames_DecodesWithOriginalTimestamps()
    {
        var (file, _) = EncodeAndWrite(false, bFrames: true, editList: true, fastStart: true);
        Assert.True(file.AsSpan().IndexOf("moov"u8) < file.AsSpan().IndexOf("mdat"u8));
        var directory = Environment.GetEnvironmentVariable("MP4BOXKIT_ARTIFACTS_DIR")
            ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts"));
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "h264-bframes-faststart.mp4"), file);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NegativeEncoderDts_WithEditList_Decodes(bool fragmented)
    {
        var result = EncodeAndWrite(fragmented, bFrames: true, editList: true);
        Assert.Equal(-2, result.Packets[0].Dts);
        using var input = new MemoryStream(result.File);
        var reader = new Mp4Reader(input);
        foreach (var expected in result.Packets)
        {
            var sample = await reader.ReadSampleAsync();
            Assert.NotNull(sample);
            Assert.Equal(expected.Pts, sample.Pts);
            Assert.Equal(expected.Dts, sample.Dts);
            Assert.Equal(expected.Data, sample.Data.ToArray());
        }
        Assert.Null(await reader.ReadSampleAsync());
    }

    [Fact]
    public async Task DelayedPresentation_WithBFrames_MatchesNativeDemuxer()
    {
        var result = EncodeAndWrite(false, bFrames: true, editList: true,
            fastStart: true, presentationDelay: 50);
        using var input = new MemoryStream(result.File);
        var reader = new Mp4Reader(input);
        foreach (var expected in result.Packets)
        {
            var sample = await reader.ReadSampleAsync();
            Assert.NotNull(sample);
            Assert.Equal(expected.Pts, sample.Pts);
            Assert.Equal(expected.Dts, sample.Dts);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Av1_AvPackets_AreWrittenAndRead(bool fragmented)
    {
        var (file, expected) = EncodeAndWrite(fragmented, videoCodec: Codec.Av1);
        using var input = new MemoryStream(file);
        var reader = new Mp4Reader(input);
        await reader.ReadContainerInfoAsync();
        Assert.Equal(Codec.Av1, Assert.Single(reader.Info.Tracks).Codec);
        foreach (var item in expected)
        {
            var sample = await reader.ReadSampleAsync();
            Assert.NotNull(sample);
            Assert.Equal(item.Data, sample.Data.ToArray());
            Assert.Equal(item.Pts, sample.Pts);
            Assert.Equal(item.Dts, sample.Dts);
        }
        Assert.Null(await reader.ReadSampleAsync());
    }

    internal static unsafe (
        byte[] File,
        List<(byte[] Data, long Pts, long Dts)> Packets)
        EncodeAndWrite(
            bool fragmented,
            bool hevc = false,
            Codec? videoCodec = null,
            bool bFrames = false,
            bool editList = false,
            bool fastStart = false,
            long presentationDelay = 0)
    {
        lock (NativeLock)
        {
            FFmpegLoader.Initialize();
            var vp = videoCodec is Codec.Vp8 or Codec.Vp9;
            var av1 = videoCodec == Codec.Av1;
            var mpeg = videoCodec is Codec.Mpeg2Video or Codec.Mpeg4Video;
            var codecName = videoCodec switch
            {
                Codec.Mpeg2Video => "mpeg2video",
                Codec.Mpeg4Video => "mpeg4",
                Codec.Av1 => "libaom-av1",
                Codec.Vp8 => "libvpx",
                Codec.Vp9 => "libvpx-vp9",
                Codec.H265 => "libx265",
                _ => "libx264"
            };
            if (videoCodec is null && hevc)
            {
                codecName = "libx265";
            }

            var codec = ffmpeg.avcodec_find_encoder_by_name(codecName);
            if (codec == null)
            {
                throw new InvalidOperationException("FFmpeg must include the requested video encoder (x264/x265/libvpx/libaom).");
            }

            var context = ffmpeg.avcodec_alloc_context3(codec);
            var frame = ffmpeg.av_frame_alloc();
            var packet = ffmpeg.av_packet_alloc();
            AVBSFContext* bsf = null;
            var filtered = ffmpeg.av_packet_alloc();
            try
            {
                if (context == null || frame == null || packet == null)
                    throw new OutOfMemoryException();
                context->width = 160;
                context->height = 96;
                context->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
                context->time_base = new AVRational { num = 1, den = 10 };
                context->framerate = new AVRational { num = 10, den = 1 };
                context->gop_size = 5;
                context->max_b_frames = bFrames ? 2 : 0;
                if (videoCodec != Codec.Mpeg2Video)
                {
                    context->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
                }

                if (mpeg)
                {
                    context->bit_rate = 1000000;
                    context->qmin = 2;
                    context->qmax = 2;
                }
                else if (av1)
                {
                    context->bit_rate = 300000;
                    Check(ffmpeg.av_opt_set(context->priv_data, "cpu-used", "8", 0));
                    Check(ffmpeg.av_opt_set(context->priv_data, "lag-in-frames", "0", 0));
                }
                else if (vp)
                {
                    context->bit_rate = 300000;
                    Check(ffmpeg.av_opt_set(context->priv_data, "deadline", "realtime", 0));
                    Check(ffmpeg.av_opt_set(context->priv_data, "lag-in-frames", "0", 0));
                }
                else
                {
                    Check(ffmpeg.av_opt_set(
                        context->priv_data, "preset", "medium", 0));
                    if (!bFrames)
                    {
                        Check(ffmpeg.av_opt_set(
                            context->priv_data, "tune", "zerolatency", 0));
                    }

                    Check(ffmpeg.av_opt_set(
                        context->priv_data, "profile", "main", 0));
                    Check(ffmpeg.av_opt_set(
                        context->priv_data, "crf", "18", 0));
                    Check(ffmpeg.av_opt_set(
                        context->priv_data,
                        hevc ? "x265-params" : "x264-params",
                        hevc
                            ? "annexb=0:repeat-headers=0:pools=none:" +
                                "frame-threads=1" +
                                (bFrames
                                    ? ":bframes=2:b-adapt=0:scenecut=0"
                                    : "")
                            : "annexb=0" +
                                (bFrames
                                    ? ":bframes=2:b-adapt=0:scenecut=0"
                                    : ""),
                        0));
                }
                Check(ffmpeg.avcodec_open2(context, codec, null));
                var extra = new ReadOnlySpan<byte>(context->extradata, context->extradata_size);
                // vpcC FullBox v1: profile 0, level 1, 8-bit 4:2:0, limited range,
                // unspecified colour primaries/transfer/matrix, no initialization data.
                var configuration = mpeg
                    ? NativeAacTests.Esds(
                        extra.ToArray(),
                        videoCodec == Codec.Mpeg2Video
                            ? (byte)0x61
                            : (byte)0x20,
                        0x11)
                    : av1
                        ? BuildAv1C(extra)
                        : vp
                            ? new byte[]
                            {
                                1, 0, 0, 0, 0, 10, 0x82, 2, 2, 2, 0, 0
                            }
                            : hevc
                                ? BuildHvcC(extra)
                                : BuildAvcC(extra);
                if (!vp && !av1 && !mpeg)
                {
                    Check(ffmpeg.av_bsf_alloc(ffmpeg.av_bsf_get_by_name(
                        hevc ? "hevc_mp4toannexb" : "h264_mp4toannexb"), &bsf));
                    Check(ffmpeg.avcodec_parameters_from_context(bsf->par_in, context));
                    ffmpeg.av_free(bsf->par_in->extradata);
                    bsf->par_in->extradata = (byte*)ffmpeg.av_mallocz(
                        (ulong)(configuration.Length +
                            ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE));
                    if (bsf->par_in->extradata == null)
                    {
                        throw new OutOfMemoryException();
                    }

                    configuration.CopyTo(new Span<byte>(
                        bsf->par_in->extradata, configuration.Length));
                    bsf->par_in->extradata_size = configuration.Length;
                    bsf->time_base_in = context->time_base;
                    Check(ffmpeg.av_bsf_init(bsf));
                }
                frame->format = (int)context->pix_fmt;
                frame->width = context->width;
                frame->height = context->height;
                Check(ffmpeg.av_frame_get_buffer(frame, 32));

                using var output = new MemoryStream();
                using Mp4Writer writer = fragmented
                    ? new Mp4FragmentedWriter(new StreamMp4Output(output))
                    : new Mp4FileWriter(new StreamMp4Output(output),
                        new Mp4FileWriterOptions { FastStart = fastStart });
                var track = writer.AddVideo(new VideoTrackOptions(
                    videoCodec ?? (hevc ? Codec.H265 : Codec.H264),
                    160,
                    96,
                    10,
                    configuration,
                    editList
                        ? new TrackTimeline(
                            -2,
                            PresentationDelay: presentationDelay)
                        : null));
                var expected = new List<(byte[] Data, long Pts, long Dts)>();
                long origin = editList ? 0 : long.MinValue;
                for (var i = 0; i < 20; i++)
                {
                    Check(ffmpeg.av_frame_make_writable(frame));
                    for (var plane = 0; plane < 3; plane++)
                    {
                        var height = plane == 0 ? 96 : 48;
                        var width = plane == 0 ? 160 : 80;
                        for (var y = 0; y < height; y++)
                            new Span<byte>(frame->data[(uint)plane] + y * frame->linesize[(uint)plane], width)
                                .Fill((byte)(plane == 0 ? 16 + i * 8 : 128));
                    }
                    frame->pts = i;
                    Check(ffmpeg.avcodec_send_frame(context, frame));
                    Drain(context, packet, bsf, filtered, writer, track, expected, ref origin);
                }
                Check(ffmpeg.avcodec_send_frame(context, null));
                Drain(context, packet, bsf, filtered, writer, track, expected, ref origin);
                if (bsf != null)
                {
                    Check(ffmpeg.av_bsf_send_packet(bsf, null));
                    DrainFilter(bsf, filtered, writer, track, expected);
                }
                writer.Complete();
                Assert.Equal(20, expected.Count);
                if (presentationDelay != 0)
                    for (var i = 0; i < expected.Count; i++)
                        expected[i] = (expected[i].Data, expected[i].Pts + presentationDelay,
                            expected[i].Dts + presentationDelay);
                var codecId = mpeg
                    ? videoCodec == Codec.Mpeg2Video
                        ? AVCodecID.AV_CODEC_ID_MPEG2VIDEO
                        : AVCodecID.AV_CODEC_ID_MPEG4
                    : av1
                        ? AVCodecID.AV_CODEC_ID_AV1
                        : vp
                            ? videoCodec == Codec.Vp9
                                ? AVCodecID.AV_CODEC_ID_VP9
                                : AVCodecID.AV_CODEC_ID_VP8
                            : hevc
                                ? AVCodecID.AV_CODEC_ID_HEVC
                                : AVCodecID.AV_CODEC_ID_H264;
                NativeVideoValidator.AssertDecodes(
                    output.ToArray(),
                    expected.Count,
                    codecId,
                    expected);
                return (output.ToArray(), expected);
            }
            finally
            {
                ffmpeg.av_packet_free(&packet);
                ffmpeg.av_packet_free(&filtered);
                ffmpeg.av_bsf_free(&bsf);
                ffmpeg.av_frame_free(&frame);
                ffmpeg.avcodec_free_context(&context);
            }
        }
    }

    private static unsafe void Drain(AVCodecContext* context, AVPacket* packet,
        AVBSFContext* bsf, AVPacket* filtered, Mp4Writer writer,
        TrackId track, List<(byte[] Data, long Pts, long Dts)> expected, ref long origin)
    {
        while (true)
        {
            var result = ffmpeg.avcodec_receive_packet(context, packet);
            if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN) || result == ffmpeg.AVERROR_EOF)
                return;
            Check(result);
            try
            {
                // Shift PTS and DTS equally; preserve composition offsets and order.
                if (origin == long.MinValue)
                    origin = packet->dts;
                packet->pts -= origin;
                packet->dts -= origin;
                if (bsf == null)
                {
                    var data = new ReadOnlySpan<byte>(packet->data, packet->size).ToArray();
                    writer.WriteSample(track, data, packet->pts, packet->dts,
                        (packet->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0 ? SampleFlags.KeyFrame : SampleFlags.None);
                    expected.Add((data, packet->pts, packet->dts));
                    continue;
                }
                Check(ffmpeg.av_bsf_send_packet(bsf, packet));
                DrainFilter(bsf, filtered, writer, track, expected);
            }
            finally { ffmpeg.av_packet_unref(packet); }
        }
    }

    private static unsafe void DrainFilter(AVBSFContext* bsf, AVPacket* packet, Mp4Writer writer,
        TrackId track, List<(byte[] Data, long Pts, long Dts)> expected)
    {
        while (true)
        {
            var result = ffmpeg.av_bsf_receive_packet(bsf, packet);
            if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN) || result == ffmpeg.AVERROR_EOF)
                return;
            Check(result);
            try
            {
                var data = PackAnnexB(new ReadOnlySpan<byte>(packet->data, packet->size),
                    bsf->par_in->codec_id == AVCodecID.AV_CODEC_ID_HEVC);
                writer.WriteSample(track, data, packet->pts, packet->dts,
                    (packet->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0 ? SampleFlags.KeyFrame : SampleFlags.None);
                expected.Add((data, packet->pts, packet->dts));
            }
            finally { ffmpeg.av_packet_unref(packet); }
        }
    }

    private static byte[] PackAnnexB(ReadOnlySpan<byte> data, bool hevc)
    {
        using var output = new MemoryStream();
        var start = -1;
        for (var i = 0; i + 2 < data.Length; i++)
        {
            if (data[i] != 0 || data[i + 1] != 0)
                continue;
            var prefix = data[i + 2] == 1 ? 3 :
                i + 3 < data.Length && data[i + 2] == 0 && data[i + 3] == 1 ? 4 : 0;
            if (prefix == 0)
                continue;
            if (start >= 0)
                WriteSampleNal(output, data[start..i], hevc);
            start = i + prefix;
            i = start - 1;
        }
        if (start < 0)
            throw new InvalidDataException("BSF did not produce Annex B.");
        WriteSampleNal(output, data[start..], hevc);
        return output.ToArray();
    }

    private static void WriteSampleNal(Stream output, ReadOnlySpan<byte> nal, bool hevc)
    {
        // avc1 parameter sets are stored in avcC, not repeated in samples.
        if (nal.IsEmpty || (hevc ? ((nal[0] >> 1) & 63) is 32 or 33 or 34 : (nal[0] & 31) is 7 or 8))
            return;
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length, (uint)nal.Length);
        output.Write(length);
        output.Write(nal);
    }

    private static byte[] BuildAvcC(ReadOnlySpan<byte> extra)
    {
        if (extra.Length > 0 && extra[0] == 1)
            return extra.ToArray();
        // With annexb=0 libx264 exposes length-prefixed SPS/PPS extradata.
        var sps = new List<byte[]>();
        var pps = new List<byte[]>();
        while (extra.Length >= 4)
        {
            var size = checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(extra));
            extra = extra[4..];
            if (size <= 0 || size > extra.Length)
                throw new InvalidDataException("Invalid length-prefixed encoder extradata.");
            var nal = extra[..size];
            if ((nal[0] & 31) == 7)
                sps.Add(nal.ToArray());
            if ((nal[0] & 31) == 8)
                pps.Add(nal.ToArray());
            extra = extra[size..];
        }
        if (!extra.IsEmpty || sps.Count == 0 || pps.Count == 0 || sps[0].Length < 4)
            throw new InvalidDataException("Encoder did not provide SPS/PPS.");
        using var buffer = new MemoryStream();
        buffer.Write([1, sps[0][1], sps[0][2], sps[0][3], 0xff, (byte)(0xe0 | sps.Count)]);
        foreach (var nal in sps)
            WriteNal(buffer, nal);
        buffer.WriteByte((byte)pps.Count);
        foreach (var nal in pps)
            WriteNal(buffer, nal);
        return buffer.ToArray();
    }

    private static byte[] BuildAv1C(ReadOnlySpan<byte> extra)
    {
        // Test-only adapter for the fixed encoder configuration above, not a
        // general sequence-header parser. Some libaom builds leave extradata empty
        // and include the sequence header in the first keyframe; configOBUs is optional.
        // This fixture is deliberately restricted to Main 8-bit 4:2:0 and level 2.0.
        // Preserve the encoder's sequence header OBUs for decoder initialization.
        return new byte[] { 0x81, 0, 0x0c, 0 }.Concat(extra.ToArray()).ToArray();
    }

    // Test adapter for the fixed Main, 8-bit, 4:2:0 encoder configuration above.
    private static byte[] BuildHvcC(ReadOnlySpan<byte> extra)
    {
        using var annexB = new MemoryStream();
        while (extra.Length >= 4)
        {
            var size = checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(extra));
            extra = extra[4..];
            if (size <= 0 || size > extra.Length)
                throw new InvalidDataException("Invalid HEVC length-prefixed extradata.");
            annexB.Write([0, 0, 0, 1]);
            annexB.Write(extra[..size]);
            extra = extra[size..];
        }
        if (!extra.IsEmpty)
            throw new InvalidDataException("Truncated HEVC extradata.");
        extra = annexB.ToArray();
        var sets = new Dictionary<int, List<byte[]>>();
        var start = -1;
        for (var i = 0; i + 2 < extra.Length; i++)
        {
            if (extra[i] != 0 || extra[i + 1] != 0)
                continue;
            var prefix = extra[i + 2] == 1 ? 3 :
                i + 3 < extra.Length && extra[i + 2] == 0 && extra[i + 3] == 1 ? 4 : 0;
            if (prefix == 0)
                continue;
            if (start >= 0)
                Add(extra[start..i]);
            start = i + prefix;
            i = start - 1;
        }
        if (start < 0)
            throw new InvalidDataException("Expected HEVC Annex B extradata.");
        Add(extra[start..]);
        Assert.True(sets.ContainsKey(32) && sets.ContainsKey(33) && sets.ContainsKey(34));
        var sps = sets[33][0];
        var rbsp = new List<byte>();
        for (var i = 2; i < sps.Length; i++)
        {
            if (i >= 4 && sps[i] == 3 && sps[i - 1] == 0 && sps[i - 2] == 0)
                continue;
            rbsp.Add(sps[i]);
        }
        Assert.True(rbsp.Count >= 13);
        using var output = new MemoryStream();
        output.WriteByte(1);
        output.Write(rbsp.Skip(1).Take(12).ToArray()); // general profile/tier/level
        output.Write([0xf0, 0, 0xfc, 0xfd, 0xf8, 0xf8, 0, 0]);
        var layers = ((rbsp[0] >> 1) & 7) + 1;
        output.WriteByte((byte)((layers << 3) | ((rbsp[0] & 1) << 2) | 3));
        output.WriteByte(3);
        foreach (var type in new[] { 32, 33, 34 })
        {
            output.WriteByte((byte)(0x80 | type));
            output.WriteByte(0);
            output.WriteByte(checked((byte)sets[type].Count));
            foreach (var nal in sets[type])
                WriteNal(output, nal);
        }
        return output.ToArray();

        void Add(ReadOnlySpan<byte> nal)
        {
            if (nal.Length < 2)
                return;
            var type = (nal[0] >> 1) & 63;
            if (type is not (32 or 33 or 34))
                return;
            if (!sets.TryGetValue(type, out var list))
                sets[type] = list = [];
            list.Add(nal.ToArray());
        }
    }

    private static void WriteNal(Stream output, byte[] nal)
    {
        Span<byte> length = stackalloc byte[2];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(length, checked((ushort)nal.Length));
        output.Write(length);
        output.Write(nal);
    }

    private static void Check(int result)
    {
        if (result < 0)
            throw new InvalidOperationException($"FFmpeg call failed: {result}");
    }
}
