using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class FragmentMemoryTests
{
    [Fact]
    public void BufferBudgetIsReusedAcrossManySegments()
    {
        var segments = 0;
        using var writer = new Mp4FragmentedWriter(
            new Mp4FragmentedWriterOptions
            {
                FragmentBoundary = Fmp4FragmentBoundary.Manual,
                MaxFragmentBytes = 6,
                MaxFragmentSamples = 2
            },
            segment =>
            {
                if (segment.Kind == Fmp4SegmentKind.Media)
                {
                    segments++;
                }
            });
        var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
        for (var i = 0; i < 2000; i++)
        {
            writer.WriteSample(track, new byte[] { 0, 1, 65 }, i * 2000L, i * 2000L);
            writer.WriteSample(track, new byte[] { 0, 1, 66 }, i * 2000L + 1000, i * 2000L + 1000);
            Assert.Throws<InvalidOperationException>(() =>
                writer.WriteSample(track, new byte[] { 0, 1, 67 }, i * 2000L + 1500, i * 2000L + 1500));
            writer.FlushFragment();
        }
        writer.Complete();
        Assert.Equal(2000, segments);
    }
}
