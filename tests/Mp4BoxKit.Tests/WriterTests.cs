using Mp4BoxKit;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class WriterTests
{
    [Fact]
    public void FragmentedWriterEmitsInitAndMediaBoxes()
    {
        using var output = new MemoryStream();
        using var writer = new Mp4FragmentedWriter(new StreamMp4Output(output));
        var track = writer.AddVideo(new VideoTrackOptions(Codec.H264, 1920, 1080, 90_000, TestMedia.AvcC));
        writer.WriteSample(track, TestMedia.VideoSample(), 0, 0, SampleFlags.KeyFrame);
        writer.Complete();
        var bytes = output.ToArray();
        Assert.Contains("ftyp", System.Text.Encoding.ASCII.GetString(bytes));
        Assert.Contains("moov", System.Text.Encoding.ASCII.GetString(bytes));
        Assert.Contains("moof", System.Text.Encoding.ASCII.GetString(bytes));
        Assert.Contains("mdat", System.Text.Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public async Task FragmentedWriterRoundTripsSamplesThroughStreamingReader()
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddVideo(new VideoTrackOptions(Codec.H264, 16, 16, CodecConfiguration: TestMedia.AvcC));
            writer.WriteSample(track, TestMedia.VideoSample(10), 40, 20, SampleFlags.KeyFrame);
            writer.Complete();
        }

        output.Position = 0;
        var reader = new Mp4Reader(output);
        var samples = new List<(TrackId Track, long Pts, long Dts, SampleFlags Flags, byte[] Data)>();
        var count = await reader.ReadSamplesAsync((in SampleInfo info, ReadOnlySpan<byte> data) =>
            samples.Add((info.Track, info.Pts, info.Dts, info.Flags, data.ToArray())));

        Assert.Equal(1, count);
        var sample = Assert.Single(samples);
        Assert.Equal(new TrackId(1), sample.Track);
        Assert.Equal(40, sample.Pts);
        Assert.Equal(20, sample.Dts);
        Assert.Equal(SampleFlags.KeyFrame, sample.Flags);
        Assert.Equal(TestMedia.VideoSample(10).ToArray(), sample.Data);
    }

    [Fact]
    public async Task AutoReaderProbesAndReadsFragmentedMp4()
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddVideo(new VideoTrackOptions(Codec.H264, 16, 16, CodecConfiguration: TestMedia.AvcC));
            writer.WriteSample(
                track,
                TestMedia.VideoSample(10),
                40,
                20,
                SampleFlags.KeyFrame);
            writer.Complete();
        }

        output.Position = 0;
        var reader = new Mp4Reader(output);
        var samples = new List<SampleInfo>();

        var count = await reader.ReadSamplesAutoAsync(
            (in SampleInfo info, ReadOnlySpan<byte> _) =>
                samples.Add(info));

        Assert.True(reader.Info.IsFragmented);
        Assert.Equal(1, count);
        Assert.Equal(40, Assert.Single(samples).Pts);
    }

    [Fact]
    public async Task UnifiedReaderAdvancesAcrossFragmentedSegments()
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddVideo(new VideoTrackOptions(Codec.H264, 16, 16, CodecConfiguration: TestMedia.AvcC));
            writer.WriteSample(track, TestMedia.VideoSample(1), 0, 0, SampleFlags.KeyFrame);
            writer.WriteSample(track, TestMedia.VideoSample(3, keyFrame: false), 3_600, 3_600);
            writer.FlushFragment();
            writer.WriteSample(track, TestMedia.VideoSample(5), 7_200, 7_200, SampleFlags.KeyFrame);
            writer.Complete();
        }

        output.Position = 0;
        var reader = new Mp4Reader(output);
        var samples = new List<Mp4Sample>();

        while (await reader.ReadSampleAsync() is { } sample)
        {
            samples.Add(sample);
        }

        Assert.Equal(3, samples.Count);
        Assert.Equal(TestMedia.VideoSample(1).ToArray(), samples[0].Data.ToArray());
        Assert.Equal(TestMedia.VideoSample(3, keyFrame: false).ToArray(), samples[1].Data.ToArray());
        Assert.Equal(TestMedia.VideoSample(5).ToArray(), samples[2].Data.ToArray());
        Assert.Equal(0, samples[0].Dts);
        Assert.Equal(3_600, samples[1].Dts);
        Assert.Equal(7_200, samples[2].Dts);
    }

    [Fact]
    public async Task SeekMovesFragmentedReaderToRequestedTime()
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(
            new StreamMp4Output(output)))
        {
            var track = writer.AddVideo(
                new VideoTrackOptions(Codec.H264, 16, 16, CodecConfiguration: TestMedia.AvcC));
            writer.WriteSample(
                track,
                TestMedia.VideoSample(1),
                0,
                0,
                SampleFlags.KeyFrame);
            writer.WriteSample(
                track, TestMedia.VideoSample(2, keyFrame: false), 3_600, 3_600);
            writer.FlushFragment();
            writer.WriteSample(
                track,
                TestMedia.VideoSample(3),
                7_200,
                7_200,
                SampleFlags.KeyFrame);
            writer.Complete();
        }

        output.Position = 0;
        var reader = new Mp4Reader(output);
        await reader.SeekAsync(
            TimeSpan.FromSeconds(0.06),
            new TrackId(1));

        var sample = await reader.ReadSampleAsync();

        Assert.NotNull(sample);
        // Decode starts at the preceding keyframe, not at the following GOP.
        Assert.Equal(TestMedia.VideoSample(1).ToArray(), sample.Data.ToArray());
        Assert.Equal(0, sample.Dts);
    }

    [Fact]
    public async Task ReaderUsesTfhdDefaultsWhenTrunOmitsSampleFields()
    {
        using var initOutput = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(
            new StreamMp4Output(initOutput)))
        {
            writer.AddVideo(new VideoTrackOptions(Codec.H264, 16, 16, CodecConfiguration: TestMedia.AvcC));
            _ = writer.InitializationSegment;
        }

        var media = BuildFragmentWithTfhdDefaults();
        var file = new byte[initOutput.Length + media.Length];
        initOutput.ToArray().CopyTo(file, 0);
        media.CopyTo(file, (int)initOutput.Length);

        using var input = new MemoryStream(file);
        var reader = new Mp4Reader(input);
        var samples = new List<Mp4Sample>();

        while (await reader.ReadSampleAsync() is { } sample)
        {
            samples.Add(sample);
        }

        Assert.Equal(2, samples.Count);
        Assert.Equal(new byte[] { 1, 2 }, samples[0].Data.ToArray());
        Assert.Equal(new byte[] { 3, 4 }, samples[1].Data.ToArray());
        Assert.Equal(3_600, samples[0].Duration);
        Assert.Equal(3_600, samples[1].Duration);
        Assert.Equal(SampleFlags.KeyFrame, samples[0].Flags);
        Assert.Equal(SampleFlags.KeyFrame, samples[1].Flags);
    }

    private static byte[] BuildFragmentWithTfhdDefaults()
    {
        var mfhd = MakeBox(
            BoxTypes.Mfhd,
            UInt32(0),
            UInt32(1));
        var tfhd = MakeBox(
            BoxTypes.Tfhd,
            UInt32(0x38),
            UInt32(1),
            UInt32(3_600),
            UInt32(2),
            UInt32(0));
        var tfdt = MakeBox(
            BoxTypes.Tfdt,
            UInt32(0x01000000),
            UInt64(0));
        var trun = MakeBox(
            BoxTypes.Trun,
            UInt32(1),
            UInt32(2),
            Int32(0));
        var traf = MakeBox(BoxTypes.Traf, tfhd, tfdt, trun);
        var moof = MakeBox(BoxTypes.Moof, mfhd, traf);
        // data_offset is relative to the first traf's implicit moof base.
        var runTypeOffset = moof.AsSpan().IndexOf("trun"u8);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(
            moof.AsSpan(runTypeOffset + 12), moof.Length + 8);
        var mdat = MakeBox(
            BoxTypes.Mdat,
            new byte[] { 1, 2, 3, 4 });

        return Combine(moof, mdat);
    }

    private static byte[] MakeBox(FourCc type, params byte[][] payloads)
    {
        var payloadLength = payloads.Sum(payload => payload.Length);
        var result = new byte[8 + payloadLength];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
            result,
            checked((uint)result.Length));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
            result.AsSpan(4),
            type.Value);

        var offset = 8;
        foreach (var payload in payloads)
        {
            payload.CopyTo(result, offset);
            offset += payload.Length;
        }

        return result;
    }

    private static byte[] Combine(params byte[][] parts)
    {
        var result = new byte[parts.Sum(part => part.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }

    private static byte[] UInt32(uint value)
    {
        var result = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(result, value);
        return result;
    }

    private static byte[] UInt64(ulong value)
    {
        var result = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(result, value);
        return result;
    }

    private static byte[] Int32(int value) => UInt32(unchecked((uint)value));

    [Fact]
    public async Task FragmentedWriterStartsNewFragmentAtVideoKeyframe()
    {
        using var output = new MemoryStream();

        using (var writer = new Mp4FragmentedWriter(
            new StreamMp4Output(output)))
        {
            var track = writer.AddVideo(
                new VideoTrackOptions(Codec.H264, 16, 16, CodecConfiguration: TestMedia.AvcC));

            WriteVideoSample(writer, track, 0, SampleFlags.KeyFrame);
            WriteVideoSample(writer, track, 3_600, SampleFlags.None);
            WriteVideoSample(writer, track, 7_200, SampleFlags.KeyFrame);
            WriteVideoSample(writer, track, 10_800, SampleFlags.None);
            WriteVideoSample(writer, track, 14_400, SampleFlags.KeyFrame);
            writer.Complete();
        }

        var bytes = output.ToArray();
        Assert.Equal(3, CountTopLevelBox(bytes, BoxTypes.Moof));
        Assert.Equal(3, CountTopLevelBox(bytes, BoxTypes.Mdat));

        output.Position = 0;
        var reader = new Mp4Reader(output);
        var samples = new List<SampleInfo>();
        var count = await reader.ReadSamplesAsync(
            (in SampleInfo info, ReadOnlySpan<byte> _) =>
                samples.Add(info));

        Assert.Equal(5, count);
        Assert.Equal(5, samples.Count);
        Assert.Equal(SampleFlags.KeyFrame, samples[0].Flags);
        Assert.Equal(SampleFlags.KeyFrame, samples[2].Flags);
        Assert.Equal(SampleFlags.KeyFrame, samples[4].Flags);
        Assert.Equal(0, samples[0].Dts);
        Assert.Equal(7_200, samples[2].Dts);
        Assert.Equal(14_400, samples[4].Dts);
    }

    [Fact]
    public void FragmentedWriterCanDeliverSegmentsWithoutStreamOutput()
    {
        var segments = new List<Fmp4Segment>();
        var options = new Mp4FragmentedWriterOptions
        {
            FragmentBoundary = Fmp4FragmentBoundary.Manual
        };

        using var writer = new Mp4FragmentedWriter(
            options,
            segments.Add);

        var track = writer.AddVideo(
            new VideoTrackOptions(Codec.H264, 16, 16, CodecConfiguration: TestMedia.AvcC));

        writer.WriteSample(
            track,
            TestMedia.VideoSample(),
            0,
            0,
            SampleFlags.KeyFrame);

        Assert.Single(segments);
        Assert.Equal(Fmp4SegmentKind.Initialization, segments[0].Kind);

        var initialization = writer.InitializationSegment;

        Assert.Single(segments);
        Assert.Equal(Fmp4SegmentKind.Initialization, segments[0].Kind);
        Assert.Equal(initialization, segments[0].Data);

        writer.FlushFragment();

        Assert.Equal(2, segments.Count);
        Assert.Equal(Fmp4SegmentKind.Media, segments[1].Kind);
        Assert.Equal(1u, segments[1].SequenceNumber);
        Assert.Contains("moof", System.Text.Encoding.ASCII.GetString(
            segments[1].Data.Span));
        Assert.Contains("mdat", System.Text.Encoding.ASCII.GetString(
            segments[1].Data.Span));
    }

    [Fact]
    public void FragmentedWriterWritesTypedMultiTrackFragmentStructure()
    {
        var segments = new List<Fmp4Segment>();
        var options = new Mp4FragmentedWriterOptions
        {
            FragmentBoundary = Fmp4FragmentBoundary.Manual
        };

        using var writer = new Mp4FragmentedWriter(options, segments.Add);
        var video = writer.AddVideo(
            new VideoTrackOptions(Codec.H264, 1280, 720, 90_000,
                CodecConfiguration: TestMedia.AvcC));
        var audio = writer.AddAudio(
            new AudioTrackOptions(Codec.Aac, 2, 48_000,
                CodecConfiguration: TestMedia.Esds));

        var videoSample = TestMedia.VideoSample(1);
        var audioSample = new byte[] { 3, 4, 5 };
        writer.WriteSample(video, videoSample, 0, 0, SampleFlags.KeyFrame);
        writer.WriteSample(audio, audioSample, 0, 0);
        writer.FlushFragment();

        var initialization = Assert.Single(
            segments,
            segment => segment.Kind == Fmp4SegmentKind.Initialization);
        var media = Assert.Single(
            segments,
            segment => segment.Kind == Fmp4SegmentKind.Media);

        var initTypes = ReadBoxTypes(initialization.Data.Span);
        Assert.Equal(new[] { BoxTypes.Ftyp, BoxTypes.Moov }, initTypes);
        var moov = ReadChild(initialization.Data.Span, BoxTypes.Moov);
        Assert.Equal(2, ReadChildren(moov.Payload, BoxTypes.Trak).Count);

        var moof = ReadChild(
            media.Data.Span,
            BoxTypes.Moof);
        var moofChildren = ReadBoxTypes(moof.Payload);

        Assert.Equal(1, moofChildren.Count(type => type == BoxTypes.Mfhd));
        Assert.Equal(2, moofChildren.Count(type => type == BoxTypes.Traf));

        foreach (var traf in ReadChildren(moof.Payload, BoxTypes.Traf))
        {
            var trafTypes = ReadBoxTypes(traf.Payload);
            Assert.Contains(BoxTypes.Tfhd, trafTypes);
            Assert.Contains(BoxTypes.Tfdt, trafTypes);
            Assert.Contains(BoxTypes.Trun, trafTypes);
        }

        var mdat = ReadChild(media.Data.Span, BoxTypes.Mdat);
        Assert.Equal(videoSample.ToArray().Concat(audioSample), mdat.Payload.ToArray());
    }

    [Fact]
    public async Task ReaderProbesVideoAndAudioTrackMetadata()
    {
        var bytes = CreateMultiTrackInitializationSegment();
        using var input = new MemoryStream(bytes);
        var reader = new Mp4Reader(input);

        var result = await reader.ReadContainerInfoAsync();

        Assert.Equal(ProbeStatus.Complete, result.Status);
        Assert.True(
            reader.Info.Tracks.Count == 2,
            $"tracks={reader.Info.Tracks.Count}, bytes={Convert.ToHexString(bytes)}");

        var video = Assert.IsType<VideoTrackInfo>(reader.Info.Tracks[0]);
        Assert.Equal(new TrackId(1), video.Id);
        Assert.Equal(Codec.H264, video.Codec);
        Assert.Equal(1280, video.Width);
        Assert.Equal(720, video.Height);
        Assert.Equal(90_000u, video.TimeScale);

        var audio = Assert.IsType<AudioTrackInfo>(reader.Info.Tracks[1]);
        Assert.Equal(new TrackId(2), audio.Id);
        Assert.Equal(Codec.Aac, audio.Codec);
        Assert.True(
            audio.Channels == 2,
            $"channels={audio.Channels}, rate={audio.SampleRate}, codec={audio.Codec}");
        Assert.Equal(48_000, audio.SampleRate);
        Assert.Equal(48_000u, audio.TimeScale);
    }

    private static byte[] CreateMultiTrackInitializationSegment()
    {
        using var writer = new Mp4FragmentedWriter(
            new Mp4FragmentedWriterOptions { FragmentBoundary = Fmp4FragmentBoundary.Manual },
            _ => { });

        writer.AddVideo(new VideoTrackOptions(Codec.H264, 1280, 720,
            CodecConfiguration: TestMedia.AvcC));
        writer.AddAudio(new AudioTrackOptions(Codec.Aac, 2, 48_000,
            CodecConfiguration: TestMedia.Esds));
        return writer.InitializationSegment.ToArray();
    }

    [Fact]
    public void ClassicWriterWritesNestedTypedSampleTableStructure()
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FileWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddVideo(new VideoTrackOptions(
                Codec.H264, 32, 24, CodecConfiguration: TestMedia.AvcC));
            writer.WriteSample(track, TestMedia.VideoSample(1), 0, 0, SampleFlags.KeyFrame);
            writer.WriteSample(track, TestMedia.VideoSample(3, keyFrame: false), 1, 1);
            writer.Complete();
        }

        var moov = ReadChild(output.ToArray(), BoxTypes.Moov);
        var trak = ReadChild(moov.Payload, BoxTypes.Trak);
        var mdia = ReadChild(trak.Payload, BoxTypes.Mdia);
        var minf = ReadChild(mdia.Payload, BoxTypes.Minf);
        var stbl = ReadChild(minf.Payload, BoxTypes.Stbl);
        var sampleTableTypes = ReadBoxTypes(stbl.Payload);

        Assert.Contains(BoxTypes.Stsd, sampleTableTypes);
        Assert.Contains(BoxTypes.Stts, sampleTableTypes);
        Assert.Contains(BoxTypes.Stsc, sampleTableTypes);
        Assert.Contains(BoxTypes.Stsz, sampleTableTypes);
        Assert.Contains(BoxTypes.Stco, sampleTableTypes);
        Assert.Contains(BoxTypes.Stss, sampleTableTypes);
    }

    private static void WriteVideoSample(
        Mp4FragmentedWriter writer,
        TrackId track,
        long timestamp,
        SampleFlags flags)
    {
        writer.WriteSample(
            track,
            new byte[] { 0, 0, 0, 1, 0x65 },
            timestamp,
            timestamp,
            flags);
    }

    private static int CountTopLevelBox(
        byte[] data,
        FourCc type)
    {
        var count = 0;
        var offset = 0;

        while (offset + 8 <= data.Length)
        {
            var size = BigEndianValue(data, offset);
            var currentType = new FourCc(BigEndianValue(data, offset + 4));

            if (size < 8 || size > data.Length - offset)
            {
                break;
            }

            if (currentType == type)
            {
                count++;
            }

            offset += checked((int)size);
        }

        return count;
    }

    private static uint BigEndianValue(
        byte[] data,
        int offset)
    {
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
            data.AsSpan(offset, 4));
    }

    private static BoxSlice ReadChild(ReadOnlySpan<byte> data, FourCc type)
    {
        return Assert.Single(ReadChildren(data, type));
    }

    private static List<BoxSlice> ReadChildren(
        ReadOnlySpan<byte> data,
        FourCc? requestedType = null)
    {
        var result = new List<BoxSlice>();
        var offset = 0;

        while (offset + 8 <= data.Length)
        {
            var size = checked((int)BigEndianValue(data.ToArray(), offset));
            if (size < 8 || size > data.Length - offset)
            {
                throw new InvalidOperationException("Invalid test box.");
            }

            var type = new FourCc(BigEndianValue(data.ToArray(), offset + 4));
            if (requestedType is null || type == requestedType.Value)
            {
                result.Add(new BoxSlice(
                    type,
                    data.Slice(offset + 8, size - 8).ToArray()));
            }

            offset += size;
        }

        return result;
    }

    private static List<FourCc> ReadBoxTypes(ReadOnlySpan<byte> data)
    {
        return ReadChildren(data).Select(box => box.Type).ToList();
    }

    private readonly record struct BoxSlice(FourCc Type, byte[] Payload);

    [Fact]
    public void ClassicWriterFinalizesSampleTables()
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FileWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddVideo(new VideoTrackOptions(
                Codec.H264, 32, 24, 90_000, TestMedia.AvcC));
            writer.WriteSample(track, TestMedia.VideoSample(1), 0, 0, SampleFlags.KeyFrame);
            writer.WriteSample(track, TestMedia.VideoSample(3, keyFrame: false), 3_000, 3_000);
            writer.Complete();
        }

        var text = System.Text.Encoding.ASCII.GetString(output.ToArray());
        Assert.Contains("ftyp", text);
        Assert.Contains("mdat", text);
        Assert.Contains("moov", text);
        Assert.Contains("stts", text);
        Assert.Contains("stsc", text);
        Assert.Contains("stsz", text);
        Assert.Contains("stco", text);
        Assert.Contains("stss", text);
    }
}
