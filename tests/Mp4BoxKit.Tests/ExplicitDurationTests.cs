using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class ExplicitDurationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitDurationsSurviveGapsAndFinalization(bool fragmented)
    {
        using var output = new MemoryStream();
        using (Mp4Writer writer = fragmented
            ? new Mp4FragmentedWriter(new StreamMp4Output(output))
            : new Mp4FileWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            writer.WriteSample(track, new byte[] { 0, 0 }, 0, 0, duration: 1000);
            if (writer is Mp4FragmentedWriter fmp4)
                fmp4.FlushFragment();
            Assert.Throws<ArgumentException>(() => writer.WriteSample(track, new byte[] { 0, 0 }, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                writer.WriteSample(
                    track,
                    new byte[] { 0, 0 },
                    5000,
                    5000,
                    duration: 0));
            if (!fragmented)
            {
                Assert.Throws<ArgumentException>(() =>
                    writer.WriteSample(
                        track,
                        new byte[] { 0, 0 },
                        5000,
                        5000,
                        duration: 700));
            }
            var nextDts = fragmented ? 5000 : 1000;
            await writer.WriteSampleAsync(track, new byte[] { 0, 0 }, nextDts, nextDts, duration: 700);
            writer.Complete();
        }
        output.Position = 0;
        var reader = new Mp4Reader(output);
        var first = await reader.ReadSampleAsync();
        var second = await reader.ReadSampleAsync();
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(1000, first.Info.Duration);
        Assert.Equal(700, second.Info.Duration);
        // Classic stts describes contiguous decode time; gaps require edit lists.
        Assert.Equal(fragmented ? 5000 : 1000, second.Dts);
    }
}
