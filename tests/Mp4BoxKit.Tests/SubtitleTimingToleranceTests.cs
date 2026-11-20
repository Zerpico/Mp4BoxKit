using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class SubtitleTimingToleranceTests
{
    private static readonly byte[] First = { 0, 1, 65 };
    private static readonly byte[] Second = { 0, 1, 66 };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SmallOverlapIsRemovedKeepingEnd(bool fragmented)
    {
        var samples = await Write(fragmented, 50, writer =>
        {
            writer.WriteSample(new TrackId(1), First, 0, 0, duration: 1010);
            writer.WriteSample(new TrackId(1), Second, 1000, 1000, duration: 1000);
        });

        Assert.Equal((0L, 1010), (samples[0].Dts, samples[0].Duration));
        Assert.Equal((1010L, 1010L, 990), (samples[1].Dts, samples[1].Pts, samples[1].Duration));
    }

    [Fact]
    public async Task SmallGapIsKept()
    {
        var samples = await Write(true, 50, writer =>
        {
            writer.WriteSample(new TrackId(1), First, 0, 0, duration: 990);
            writer.WriteSample(new TrackId(1), Second, 1000, 1000, duration: 1000);
        });

        Assert.Equal((0L, 990), (samples[0].Dts, samples[0].Duration));
        Assert.Equal((1000L, 1000), (samples[1].Dts, samples[1].Duration));
    }

    [Theory]
    [InlineData(50)]
    [InlineData(0)]
    public async Task OverlapAboveToleranceIsKept(int tolerance)
    {
        var samples = await Write(true, tolerance, writer =>
        {
            writer.WriteSample(new TrackId(1), First, 0, 0, duration: tolerance == 0 ? 1010 : 1100);
            writer.WriteSample(new TrackId(1), Second, 1000, 1000, duration: 1000);
        });

        Assert.Equal((1000L, 1000), (samples[1].Dts, samples[1].Duration));
    }

    [Fact]
    public async Task OverlapWithFlushedFragmentIsRemoved()
    {
        var samples = await Write(true, 50, writer =>
        {
            writer.WriteSample(new TrackId(1), First, 0, 0, duration: 1010);
            ((Mp4FragmentedWriter)writer).FlushFragment();
            writer.WriteSample(new TrackId(1), Second, 1000, 1000, duration: 1000);
        });

        Assert.Equal((1010L, 990), (samples[1].Dts, samples[1].Duration));
    }

    [Fact]
    public async Task OverlapWithoutExplicitDurationKeepsCurrentBehavior()
    {
        var samples = await Write(true, 50, writer =>
        {
            writer.WriteSample(new TrackId(1), First, 0, 0);
            writer.WriteSample(new TrackId(1), Second, 1000, 1000, duration: 1000);
        });

        Assert.Equal((0L, 1000), (samples[0].Dts, samples[0].Duration));
        Assert.Equal(1000L, samples[1].Dts);
    }

    [Fact]
    public void SampleShorterThanOverlapIsRejected()
    {
        using var output = new MemoryStream();
        using var writer = new Mp4FragmentedWriter(new StreamMp4Output(output));
        var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g, TimingTolerance: 50));
        writer.WriteSample(track, First, 0, 0, duration: 1010);

        Assert.Throws<ArgumentException>(() =>
            writer.WriteSample(track, Second, 1000, 1000, duration: 10));
    }

    [Fact]
    public void NegativeToleranceIsRejected()
    {
        using var output = new MemoryStream();
        using var writer = new Mp4FragmentedWriter(new StreamMp4Output(output));

        Assert.Throws<ArgumentOutOfRangeException>(() => writer.AddSubtitle(
            new SubtitleTrackOptions(Codec.Tx3g, TimingTolerance: -1)));
        Assert.Empty(writer.Tracks);
    }

    private static async Task<List<Mp4Sample>> Write(bool fragmented, int tolerance, Action<Mp4Writer> write)
    {
        using var output = new MemoryStream();
        using (Mp4Writer writer = fragmented
            ? new Mp4FragmentedWriter(new StreamMp4Output(output))
            : new Mp4FileWriter(new StreamMp4Output(output)))
        {
            writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g, TimingTolerance: tolerance));
            write(writer);
            writer.Complete();
        }

        output.Position = 0;
        var reader = new Mp4Reader(output);
        var samples = new List<Mp4Sample>();
        while (await reader.ReadSampleAsync() is { } sample)
            samples.Add(sample);
        Assert.Equal(2, samples.Count);
        return samples;
    }
}
