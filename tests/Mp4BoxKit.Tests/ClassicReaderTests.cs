using Mp4BoxKit;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class ClassicReaderTests
{
    private const string FixtureName =
        "classic-h264-1280x720-127frames.mp4";

    [Fact]
    public async Task ReadsClassicMp4SampleTables()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            FixtureName);

        using var stream = File.OpenRead(fixturePath);
        var reader = new Mp4Reader(stream);
        using var probeStream = File.OpenRead(fixturePath);
        var probe = new Mp4Reader(probeStream);
        var samples = new List<ReadSample>();

        var probeResult = await probe.ReadContainerInfoAsync();

        Assert.Equal(ProbeStatus.Complete, probeResult.Status);
        var video = Assert.IsType<VideoTrackInfo>(
            Assert.Single(probe.Info.Tracks));
        Assert.Equal(1280, video.Width);
        Assert.Equal(720, video.Height);
        Assert.Equal(Codec.H264, video.Codec);

        var count = await reader.ReadSamplesAsync(
            (in SampleInfo info, ReadOnlySpan<byte> data) =>
            {
                samples.Add(new ReadSample(
                    info,
                    data.Length,
                    data[..Math.Min(data.Length, 8)].ToArray()));
            });

        Assert.Equal(127, count);
        Assert.Equal(127, samples.Count);
        Assert.All(samples, sample => Assert.True(sample.DataLength > 4));

        var first = samples[0].Info;
        var last = samples[^1].Info;

        Assert.Equal(new TrackId(1), first.Track);
        Assert.Equal(-1_024, first.Dts);
        Assert.Equal(0, first.Pts);
        Assert.Equal(512, first.Duration);
        Assert.Equal(SampleFlags.KeyFrame, first.Flags);

        Assert.Equal(63_488, last.Dts);
        Assert.Equal(512, last.Duration);
        Assert.Equal(64_512, last.Pts);
        Assert.Equal(64_000, last.Dts + last.Duration);

        var totalDuration = samples.Sum(sample => sample.Info.Duration);
        Assert.Equal(65_024, totalDuration);

        Assert.Equal(
            new byte[] { 0, 0, 2, 0x9f },
            samples[0].Prefix[..4]);

    }

    [Fact]
    public async Task AutoReaderProbesAndReadsClassicMp4()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            FixtureName);

        using var stream = File.OpenRead(fixturePath);
        var reader = new Mp4Reader(stream);
        var samples = new List<SampleInfo>();

        var count = await reader.ReadSamplesAutoAsync(
            (in SampleInfo info, ReadOnlySpan<byte> _) =>
                samples.Add(info));

        Assert.False(reader.Info.IsFragmented);
        Assert.Equal(127, count);
        Assert.Equal(127, samples.Count);
        Assert.Equal(new TrackId(1), samples[0].Track);
        Assert.Equal(-1_024, samples[0].Dts);
    }

    [Fact]
    public async Task UnifiedReaderReturnsClassicSamplesOneByOne()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            FixtureName);

        using var stream = File.OpenRead(fixturePath);
        var reader = new Mp4Reader(stream);
        var first = await reader.ReadSampleAsync();
        var second = await reader.ReadSampleAsync();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(new TrackId(1), first.Track);
        Assert.Equal(-1_024, first.Dts);
        Assert.True(second.Dts > first.Dts);
        Assert.True(first.Data.Length > 4);
    }

    [Fact]
    public async Task SeekMovesClassicReaderToRequestedTrackTime()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            FixtureName);

        using var stream = File.OpenRead(fixturePath);
        var reader = new Mp4Reader(stream);

        await reader.SeekAsync(
            TimeSpan.FromSeconds(2),
            new TrackId(1));

        var sample = await reader.ReadSampleAsync();

        Assert.NotNull(sample);
        Assert.Equal(new TrackId(1), sample.Track);
        // Seeking may return an earlier keyframe for decoder preroll. Compare
        // presentation time: DTS precedes it for this B-frame fixture.
        Assert.True(sample.Pts >= 2 * 12_800 - sample.Duration);
        Assert.Equal(SampleFlags.KeyFrame, sample.Flags);
    }

    private readonly record struct ReadSample(
        SampleInfo Info,
        int DataLength,
        byte[] Prefix);
}
