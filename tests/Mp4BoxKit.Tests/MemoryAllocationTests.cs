using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class MemoryAllocationTests
{
    private const int FragmentCount = 40;
    private const int SampleSize = 256 * 1024;

    [Fact]
    public void OutputOnlyStreamingAllocatesLessThanDeliveryBuffer()
    {
        // Warm up both paths so JIT/static initialization is not attributed to one side.
        Run(outputOnly: true, fragments: 2);
        Run(outputOnly: false, fragments: 2);

        var outputOnly = Run(outputOnly: true, fragments: FragmentCount);
        var delivered = Run(outputOnly: false, fragments: FragmentCount);

        Assert.True(delivered > outputOnly + 2_000_000,
            $"Expected the contiguous delivery buffer to allocate more: " +
            $"outputOnly={outputOnly:N0}, delivered={delivered:N0}");
    }

    private static long Run(bool outputOnly, int fragments)
    {
        var output = new NullOutput();
        var options = new Mp4FragmentedWriterOptions
        {
            FragmentBoundary = Fmp4FragmentBoundary.Manual,
            MaxFragmentBytes = 1024 * 1024
        };
        var sample = new byte[SampleSize];

        var before = GC.GetTotalAllocatedBytes(precise: true);
        using var writer = outputOnly
            ? new Mp4FragmentedWriter(output, options)
            : new Mp4FragmentedWriter(output, options, _ => { });
        var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
        for (var i = 0; i < fragments; i++)
        {
            writer.WriteSample(track, sample, i * 1000L, i * 1000L, duration: 1000);
            writer.FlushFragment();
        }
        writer.Complete();
        return GC.GetTotalAllocatedBytes(precise: true) - before;
    }

    private sealed class NullOutput : IMp4Output
    {
        public bool CanSeek => false;
        public long Position { get; private set; }

        public void Write(ReadOnlySpan<byte> data) => Position += data.Length;

        public ValueTask WriteAsync(
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Position += data.Length;
            return ValueTask.CompletedTask;
        }

        public void Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();
    }
}
