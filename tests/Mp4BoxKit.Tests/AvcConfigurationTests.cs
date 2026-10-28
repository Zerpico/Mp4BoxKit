using System.Buffers.Binary;
using Mp4BoxKit.Codecs;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class AvcConfigurationTests
{
    [Fact]
    public void CreateFromSpsPpsBuildsAvcC()
    {
        var config = AvcConfiguration.CreateFromSpsPps(
            TestMedia.RawSps.Span, TestMedia.RawPps.Span);

        Assert.Equal(16, config.Length);
        Assert.Equal(1, config.Span[0]);
        Assert.Equal(0x42, config.Span[1]);
        Assert.Equal(0x00, config.Span[2]);
        Assert.Equal(0x1E, config.Span[3]);
        Assert.Equal(0xFF, config.Span[4]);
        Assert.Equal(0xE1, config.Span[5]);
        Assert.Equal(4, BinaryPrimitives.ReadUInt16BigEndian(config.Span[6..]));
        Assert.Equal(1, config.Span[12]); // PPS count
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(config.Span[13..]));
    }

    [Fact]
    public void HighProfileAddsAvcCExtension()
    {
        var config = AvcConfiguration.CreateFromSpsPps(
            new byte[] { 0x67, 0x64, 0x00, 0x28 }, // High profile
            new byte[] { 0x68 });

        Assert.Equal(20, config.Length);
        Assert.Equal(new byte[] { 0xFD, 0xF8, 0xF8, 0x00 },
            config.Span[^4..].ToArray());
    }

    [Fact]
    public void CreateFromSpsPpsRejectsAnnexBInput()
    {
        Assert.Throws<ArgumentException>(() => AvcConfiguration.CreateFromSpsPps(
            new byte[] { 0, 0, 0, 1, 0x67, 0x42, 0x00, 0x1E },
            TestMedia.RawPps.Span));
    }

    [Fact]
    public void FromNalUnitsFiltersParameterSets()
    {
        var sample = AvcSample.FromNalUnits(new[]
        {
            TestMedia.RawSps,
            TestMedia.RawPps,
            new ReadOnlyMemory<byte>(new byte[] { 0x65, 0x88 })
        });

        Assert.Equal(new byte[] { 0, 0, 0, 2, 0x65, 0x88 }, sample.ToArray());
    }

    [Fact]
    public void FromAnnexBFiltersParameterSets()
    {
        var sample = AvcSample.FromAnnexB(new byte[]
        {
            0, 0, 0, 1, 0x67, 0x42, 0x00, 0x1E,
            0, 0, 1, 0x68,
            0, 0, 0, 1, 0x65, 0x88
        });

        Assert.Equal(new byte[] { 0, 0, 0, 2, 0x65, 0x88 }, sample.ToArray());
    }

    [Fact]
    public async Task WriteSampleNalUnitsFiltersParameterSets()
    {
        var configuration = AvcConfiguration.CreateFromSpsPps(
            TestMedia.RawSps.Span, TestMedia.RawPps.Span);
        var idr = new ReadOnlyMemory<byte>(new byte[] { 0x65, 0x88 });

        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddVideo(new VideoTrackOptions(
                Codec.H264, 16, 16, CodecConfiguration: configuration));
            writer.WriteSampleNalUnits(track,
                new[] { TestMedia.RawSps, TestMedia.RawPps, idr },
                0, 0, SampleFlags.KeyFrame);
            writer.Complete();
        }

        output.Position = 0;
        var sample = await new Mp4Reader(output).ReadSampleAsync();
        Assert.NotNull(sample);
        Assert.Equal(AvcSample.FromNalUnits(
            new[] { TestMedia.RawSps, TestMedia.RawPps, idr }).ToArray(),
            sample.Data.ToArray());
    }

    [Fact]
    public async Task WriteSampleNalUnitsAsyncCanKeepParameterSets()
    {
        var configuration = AvcConfiguration.CreateFromSpsPps(
            TestMedia.RawSps.Span, TestMedia.RawPps.Span);
        var idr = new ReadOnlyMemory<byte>(new byte[] { 0x65, 0x88 });

        using var output = new MemoryStream();
        await using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddVideo(new VideoTrackOptions(
                Codec.H264, 16, 16, CodecConfiguration: configuration));
            await writer.WriteSampleNalUnitsAsync(track,
                new[] { TestMedia.RawSps, TestMedia.RawPps, idr },
                0, 0, SampleFlags.KeyFrame, includeParameterSets: true);
            await writer.CompleteAsync();
        }

        output.Position = 0;
        var sample = await new Mp4Reader(output).ReadSampleAsync();
        Assert.NotNull(sample);
        Assert.Equal(AvcSample.FromNalUnits(
            new[] { TestMedia.RawSps, TestMedia.RawPps, idr },
            includeParameterSets: true).ToArray(),
            sample.Data.ToArray());
    }

    [Fact]
    public async Task AccessUnitWithSpsPpsAndIdrIsOneSample()
    {
        var configuration = AvcConfiguration.CreateFromSpsPps(
            TestMedia.RawSps.Span, TestMedia.RawPps.Span);
        var accessUnit = AvcSample.FromNalUnits(new[]
        {
            TestMedia.RawSps,
            TestMedia.RawPps,
            new ReadOnlyMemory<byte>(new byte[] { 0x65, 0x88 })
        }, includeParameterSets: true);

        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddVideo(new VideoTrackOptions(
                Codec.H264, 16, 16, CodecConfiguration: configuration));
            writer.WriteSample(track, accessUnit.Span, 0, 0, SampleFlags.KeyFrame);
            writer.Complete();
        }

        output.Position = 0;
        var reader = new Mp4Reader(output);
        var sample = await reader.ReadSampleAsync();
        Assert.NotNull(sample);
        Assert.Equal(accessUnit.ToArray(), sample.Data.ToArray());
        Assert.Null(await reader.ReadSampleAsync());
    }

    [Fact]
    public async Task OutOfBandParameterSetsAndVclOnlySamplesRoundTrip()
    {
        var configuration = AvcConfiguration.CreateFromSpsPps(
            TestMedia.RawSps.Span, TestMedia.RawPps.Span);

        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddVideo(new VideoTrackOptions(
                Codec.H264, 16, 16, CodecConfiguration: configuration));
            writer.WriteSample(track, TestMedia.VideoSample(1), 0, 0, SampleFlags.KeyFrame);
            writer.WriteSample(track, TestMedia.VideoSample(2, keyFrame: false), 3_600, 3_600);
            writer.Complete();
        }

        output.Position = 0;
        var reader = new Mp4Reader(output);
        var first = await reader.ReadSampleAsync();
        var second = await reader.ReadSampleAsync();
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(0, first.Dts);
        Assert.Equal(3_600, second.Dts);
        Assert.Null(await reader.ReadSampleAsync());
    }

    [Fact]
    public async Task HelperConfigurationIsAcceptedByWriterAndReader()
    {
        var configuration = AvcConfiguration.CreateFromSpsPps(
            TestMedia.RawSps.Span, TestMedia.RawPps.Span);

        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddVideo(new VideoTrackOptions(
                Codec.H264, 16, 16, CodecConfiguration: configuration));
            writer.WriteSample(track, TestMedia.VideoSample(), 0, 0, SampleFlags.KeyFrame);
            writer.Complete();
        }

        output.Position = 0;
        var reader = new Mp4Reader(output);
        await reader.ReadContainerInfoAsync();
        var video = Assert.IsType<VideoTrackInfo>(Assert.Single(reader.Info.Tracks));
        Assert.Equal(configuration.ToArray(), video.CodecConfiguration.ToArray());
    }
}
