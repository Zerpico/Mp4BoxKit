using FFmpeg.AutoGen;
using Mp4BoxKit.Codecs;
using Xunit;

namespace Mp4BoxKit.FFmpeg.Tests;

public class NativeAacTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task DtsAndPcm_RoundTripAndDecode(bool fragmented, bool pcm)
    {
        using var input = new MemoryStream(Generate(fragmented, pcm ? Codec.Pcm : Codec.Dts));
        var reader = new Mp4Reader(input);
        await reader.ReadContainerInfoAsync();
        Assert.Equal(pcm ? Codec.Pcm : Codec.Dts, Assert.Single(reader.Info.Tracks).Codec);
        var count = 0;
        while (await reader.ReadSampleAsync() is { } sample)
        {
            Assert.False(sample.Data.IsEmpty);
            count++;
        }
        Assert.True(count >= 12);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AacPackets_RoundTripAndDecode(bool fragmented)
        => Generate(fragmented);

    [Theory]
    [InlineData(false, 48_000, 1)]
    [InlineData(true, 48_000, 1)]
    [InlineData(false, 48_000, 2)]
    [InlineData(true, 48_000, 2)]
    [InlineData(false, 44_100, 1)]
    [InlineData(true, 44_100, 2)]
    [InlineData(false, 32_000, 2)]
    [InlineData(false, 16_000, 1)]
    public void AacWithoutConfiguration_Matrix(
        bool fragmented, int sampleRate, int channels)
        => Generate(fragmented, Codec.Aac, sampleRate, channels);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Ac3AndEAc3Packets_RoundTripAndDecode(bool fragmented, bool enhanced)
    {
        var codec = enhanced ? Codec.EAc3 : Codec.Ac3;
        using var input = new MemoryStream(Generate(fragmented, codec));
        var reader = new Mp4Reader(input);
        await reader.ReadContainerInfoAsync();
        Assert.Equal(codec, Assert.Single(reader.Info.Tracks).Codec);
        var count = 0;
        while (await reader.ReadSampleAsync() is { } sample)
        {
            Assert.Equal(count * 1536L, sample.Dts);
            Assert.Equal(0x0b, sample.Data.Span[0]);
            Assert.Equal(0x77, sample.Data.Span[1]);
            count++;
        }
        Assert.Equal(12, count);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Mp3AndOpusPackets_RoundTripAndDecode(bool fragmented, bool opus)
    {
        var file = Generate(fragmented, opus ? Codec.Opus : Codec.Mp3);
        using var input = new MemoryStream(file);
        var reader = new Mp4Reader(input);
        await reader.ReadContainerInfoAsync();
        var track = Assert.IsType<AudioTrackInfo>(Assert.Single(reader.Info.Tracks));
        Assert.Equal(opus ? Codec.Opus : Codec.Mp3, track.Codec);
        var count = 0;
        long previous = -1;
        while (await reader.ReadSampleAsync() is { } sample)
        {
            Assert.True(sample.Dts > previous);
            Assert.False(sample.Data.IsEmpty);
            previous = sample.Dts;
            count++;
        }
        Assert.True(count >= 12);
    }

    internal static unsafe byte[] Generate(
        bool fragmented,
        Codec audioCodec = Codec.Aac,
        int sampleRate = 48_000,
        int channels = 1)
    {
        FFmpegLoader.Initialize();
        var codecId = audioCodec == Codec.Pcm ? AVCodecID.AV_CODEC_ID_PCM_S16LE :
            audioCodec == Codec.Dts ? AVCodecID.AV_CODEC_ID_DTS :
            audioCodec == Codec.Ac3 ? AVCodecID.AV_CODEC_ID_AC3 :
            audioCodec == Codec.EAc3 ? AVCodecID.AV_CODEC_ID_EAC3 :
            audioCodec == Codec.Opus ? AVCodecID.AV_CODEC_ID_OPUS :
            audioCodec == Codec.Mp3 ? AVCodecID.AV_CODEC_ID_MP3 : AVCodecID.AV_CODEC_ID_AAC;
        var codec = audioCodec == Codec.Opus ? ffmpeg.avcodec_find_encoder_by_name("libopus") :
            audioCodec == Codec.Mp3 ? ffmpeg.avcodec_find_encoder_by_name("libmp3lame") :
            ffmpeg.avcodec_find_encoder(codecId);
        Assert.True(codec != null);
        var context = ffmpeg.avcodec_alloc_context3(codec);
        var frame = ffmpeg.av_frame_alloc();
        var packet = ffmpeg.av_packet_alloc();
        try
        {
            if (context == null || frame == null || packet == null)
                throw new OutOfMemoryException();
            context->sample_rate = sampleRate;
            context->sample_fmt = audioCodec == Codec.Pcm ? AVSampleFormat.AV_SAMPLE_FMT_S16 :
                audioCodec == Codec.Dts ? AVSampleFormat.AV_SAMPLE_FMT_S32 :
                audioCodec == Codec.Opus ? AVSampleFormat.AV_SAMPLE_FMT_FLT : AVSampleFormat.AV_SAMPLE_FMT_FLTP;
            context->time_base = new AVRational { num = 1, den = sampleRate };
            context->bit_rate = audioCodec == Codec.Dts ? 768000 : 96000;
            if (audioCodec == Codec.Dts)
                context->strict_std_compliance = ffmpeg.FF_COMPLIANCE_EXPERIMENTAL;
            ffmpeg.av_channel_layout_default(&context->ch_layout, channels);
            context->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
            Check(ffmpeg.avcodec_open2(context, codec, null));
            var asc = new ReadOnlySpan<byte>(context->extradata, context->extradata_size).ToArray();
            frame->format = (int)context->sample_fmt;
            frame->sample_rate = context->sample_rate;
            frame->nb_samples = context->frame_size > 0 ? context->frame_size : 1024;
            Check(ffmpeg.av_channel_layout_copy(&frame->ch_layout, &context->ch_layout));
            Check(ffmpeg.av_frame_get_buffer(frame, 0));
            using var output = new MemoryStream();
            using Mp4Writer writer = fragmented
                ? new Mp4FragmentedWriter(new StreamMp4Output(output),
                    new Mp4FragmentedWriterOptions { FragmentBoundary = Fmp4FragmentBoundary.Manual })
                : new Mp4FileWriter(new StreamMp4Output(output));
            var configuration = audioCodec == Codec.Pcm ? Array.Empty<byte>() :
                audioCodec == Codec.Dts ? DtsConfiguration() :
                audioCodec is Codec.Ac3 or Codec.EAc3 ? DolbyConfiguration(audioCodec) :
                audioCodec == Codec.Opus ? OpusConfiguration(asc) :
                audioCodec == Codec.Aac ? AacConfiguration.CreateEsds(sampleRate, channels).ToArray() :
                Esds(asc, (byte)0x6b);
            var track = writer.AddAudio(new AudioTrackOptions(audioCodec, channels, sampleRate,
                TimeScale: (uint)sampleRate, CodecConfiguration: configuration,
                PcmLittleEndian16: audioCodec == Codec.Pcm));
            var count = 0;
            long origin = long.MinValue;
            for (var i = 0; i < 12; i++)
            {
                Check(ffmpeg.av_frame_make_writable(frame));
                for (var j = 0; j < frame->nb_samples; j++)
                {
                    var value = 0.2 * Math.Sin(
                        2 * Math.PI * 440 * (i * frame->nb_samples + j) / sampleRate);
                    if (audioCodec == Codec.Pcm)
                    {
                        ((short*)frame->data[0])[j] = (short)(value * short.MaxValue);
                    }
                    else if (audioCodec == Codec.Dts)
                    {
                        ((int*)frame->data[0])[j] = (int)(value * int.MaxValue);
                    }
                    else if (audioCodec == Codec.Aac)
                    {
                        for (var channel = 0; channel < channels; channel++)
                            ((float*)frame->data[(uint)channel])[j] = (float)value;
                    }
                    else
                    {
                        ((float*)frame->data[0])[j] = (float)value;
                    }
                }
                frame->pts = (long)i * frame->nb_samples;
                Check(ffmpeg.avcodec_send_frame(context, frame));
                Drain(context, packet, writer, track, ref count, ref origin);
            }
            Check(ffmpeg.avcodec_send_frame(context, null));
            Drain(context, packet, writer, track, ref count, ref origin);
            writer.Complete();
            Assert.True(count >= 12);
            var preSkip = audioCodec == Codec.Opus
                ? System.Buffers.Binary.BinaryPrimitives
                    .ReadUInt16LittleEndian(asc.AsSpan(10))
                : 0;
            Validate(
                output.ToArray(),
                count,
                frame->nb_samples,
                codecId,
                preSkip,
                sampleRate,
                channels);
            return output.ToArray();
        }
        finally
        {
            ffmpeg.av_packet_free(&packet);
            ffmpeg.av_frame_free(&frame);
            ffmpeg.avcodec_free_context(&context);
        }
    }

    private static unsafe void Drain(AVCodecContext* encoder, AVPacket* packet, Mp4Writer writer,
        TrackId track, ref int count, ref long origin)
    {
        while (true)
        {
            var result = ffmpeg.avcodec_receive_packet(encoder, packet);
            if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN) || result == ffmpeg.AVERROR_EOF)
                return;
            Check(result);
            try
            {
                // Preserve the encoder priming packet, but normalize the decode timeline.
                if (origin == long.MinValue)
                    origin = packet->dts;
                writer.WriteSample(track, new ReadOnlySpan<byte>(packet->data, packet->size),
                    packet->pts - origin, packet->dts - origin, SampleFlags.KeyFrame);
                count++;
            }
            finally { ffmpeg.av_packet_unref(packet); }
        }
    }

    private static byte[] OpusConfiguration(byte[] header)
    {
        Assert.True(header.Length >= 19);
        Assert.Equal("OpusHead", System.Text.Encoding.ASCII.GetString(header, 0, 8));
        Assert.Equal(0, header[18]); // mono/stereo mapping family
        var result = new byte[11];
        result[0] = 0; // dOps version, not OpusHead version
        result[1] = header[9];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(2),
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(10)));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4),
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12)));
        System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(result.AsSpan(8),
            System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(header.AsSpan(16)));
        return result;
    }

    private static byte[] DtsConfiguration()
    {
        // DTS core fixture: 48 kHz, 768 kb/s, 16-bit source resolution.
        var data = new byte[20];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(data, 48000);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), 768000);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), 768000);
        data[12] = 16;
        return data;
    }

    private static byte[] DolbyConfiguration(Codec codec)
    {
        // Test fixture only: 48 kHz, mono, 96 kb/s, no LFE or dependent streams.
        // AC-3 bsid=8, bsmod=0, acmod=1, bit_rate_code=6 (96 kb/s).
        if (codec == Codec.Ac3)
            return Bits((0, 2), (8, 5), (0, 3), (1, 3), (0, 1), (6, 5), (0, 5));
        // EC3SpecificBox: data_rate, num_ind_sub, then one independent substream.
        return Bits((96, 13), (0, 3), (0, 2), (16, 5), (0, 1), (0, 1),
            (0, 3), (1, 3), (0, 1), (0, 3), (0, 4), (0, 1));

        static byte[] Bits(params (int Value, int Width)[] fields)
        {
            var result = new byte[fields.Sum(f => f.Width) / 8];
            var bit = 0;
            foreach (var field in fields)
                for (var i = field.Width - 1; i >= 0; i--, bit++)
                    result[bit / 8] |= (byte)(((field.Value >> i) & 1) << (7 - bit % 8));
            return result;
        }
    }

    internal static byte[] Esds(byte[] asc, byte objectType = 0x40, byte streamType = 0x15)
    {
        byte[] Descriptor(byte tag, byte[] payload)
        {
            using var result = new MemoryStream();
            result.WriteByte(tag);
            result.WriteByte((byte)(0x80 | (payload.Length >> 21 & 127)));
            result.WriteByte((byte)(0x80 | (payload.Length >> 14 & 127)));
            result.WriteByte((byte)(0x80 | (payload.Length >> 7 & 127)));
            result.WriteByte((byte)(payload.Length & 127));
            result.Write(payload);
            return result.ToArray();
        }
        var decoder = Descriptor(4, new byte[]
            { objectType, streamType, 0, 0, 0, 0, 1, 0x77, 0, 0, 1, 0x77, 0 }
            .Concat(asc.Length == 0 ? Array.Empty<byte>() : Descriptor(5, asc)).ToArray());
        return new byte[4].Concat(Descriptor(3,
            new byte[] { 0, 1, 0 }.Concat(decoder).Concat(Descriptor(6, [2])).ToArray())).ToArray();
    }

    private static unsafe void Validate(byte[] file, int expectedPackets, int frameSize,
        AVCodecID codecId = AVCodecID.AV_CODEC_ID_AAC, int preSkip = 0,
        int sampleRate = 48_000, int channels = 1)
    {
        var path = Path.Combine(Path.GetTempPath(), $"fmp4-aac-{Guid.NewGuid():N}.mp4");
        AVFormatContext* format = null;
        AVCodecContext* decoder = null;
        var packet = ffmpeg.av_packet_alloc();
        var frame = ffmpeg.av_frame_alloc();
        try
        {
            if (packet == null || frame == null)
                throw new OutOfMemoryException();
            File.WriteAllBytes(path, file);
            Check(ffmpeg.avformat_open_input(&format, path, null, null));
            Check(ffmpeg.avformat_find_stream_info(format, null));
            var index = ffmpeg.av_find_best_stream(format, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, null, 0);
            Check(index);
            var parameters = format->streams[index]->codecpar;
            Assert.Equal(codecId, parameters->codec_id);
            Assert.Equal(sampleRate, parameters->sample_rate);
            Assert.Equal(channels, parameters->ch_layout.nb_channels);
            decoder = ffmpeg.avcodec_alloc_context3(ffmpeg.avcodec_find_decoder(parameters->codec_id));
            if (decoder == null)
                throw new OutOfMemoryException();
            Check(ffmpeg.avcodec_parameters_to_context(decoder, parameters));
            Check(ffmpeg.avcodec_open2(decoder, decoder->codec, null));
            var packets = 0;
            long samples = 0;
            int read;
            while ((read = ffmpeg.av_read_frame(format, packet)) >= 0)
            {
                try
                {
                    if (packet->stream_index != index)
                        continue;
                    packets++;
                    Check(ffmpeg.avcodec_send_packet(decoder, packet));
                    samples += Receive(decoder, frame);
                }
                finally { ffmpeg.av_packet_unref(packet); }
            }
            Assert.Equal(ffmpeg.AVERROR_EOF, read);
            Check(ffmpeg.avcodec_send_packet(decoder, null));
            samples += Receive(decoder, frame);
            Assert.Equal(expectedPackets, packets);
            Assert.Equal((long)expectedPackets * frameSize - preSkip, samples);
        }
        finally
        {
            ffmpeg.av_frame_free(&frame);
            ffmpeg.av_packet_free(&packet);
            ffmpeg.avcodec_free_context(&decoder);
            ffmpeg.avformat_close_input(&format);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static unsafe int Receive(AVCodecContext* decoder, AVFrame* frame)
    {
        var samples = 0;
        while (true)
        {
            var result = ffmpeg.avcodec_receive_frame(decoder, frame);
            if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN) || result == ffmpeg.AVERROR_EOF)
                return samples;
            Check(result);
            Assert.Equal(0, frame->decode_error_flags);
            samples += frame->nb_samples;
            ffmpeg.av_frame_unref(frame);
        }
    }

    private static void Check(int result)
    {
        if (result < 0)
            throw new InvalidOperationException($"FFmpeg failed: {result}");
    }
}
