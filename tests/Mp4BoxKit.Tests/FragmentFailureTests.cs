using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class FragmentFailureTests
{
    [Fact]
    public void CaughtReentrancyDoesNotCorruptDelivery()
    {
        Mp4FragmentedWriter? writer = null;
        var calls = 0;
        writer = new Mp4FragmentedWriter(new Mp4FragmentedWriterOptions
        { WriteRandomAccessIndex = true }, segment =>
    {
        calls++;
        Assert.Throws<InvalidOperationException>(() => writer!.FlushFragment());
        Assert.Throws<InvalidOperationException>(() => writer!.Complete());
        Assert.Throws<InvalidOperationException>(() => writer!.Dispose());
        Assert.Throws<InvalidOperationException>(() => writer!.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g)));
        Assert.Throws<InvalidOperationException>(() =>
            writer!.WriteSample(
                new TrackId(1),
                new byte[] { 0, 0 },
                1000,
                1000));
    });
        var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
        writer.WriteSample(track, new byte[] { 0, 0 }, 0, 0, duration: 1000);
        writer.Complete();
        writer.Dispose();
        Assert.Equal(3, calls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void OutputFailureNeverRetriesPartialDelivery(int failAt)
    {
        var output = new FailingOutput(failAt);
        var callbacks = 0;
        var writer = new Mp4FragmentedWriter(output, new Mp4FragmentedWriterOptions
        { WriteRandomAccessIndex = true }, _ => callbacks++);
        var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
        Assert.Throws<IOException>(() =>
        {
            writer.WriteSample(track, new byte[] { 0, 0 }, 0, 0, duration: 1000);
            writer.Complete();
        });
        Assert.Equal(failAt - 1, callbacks);
        Assert.Throws<InvalidOperationException>(() => writer.Complete());
        writer.Dispose();
        Assert.Equal(failAt, output.Calls);
    }

    private sealed class FailingOutput(int failAt) : IMp4Output
    {
        public int Calls;
        public bool CanSeek => false;
        public long Position => throw new NotSupportedException();
        public void Write(ReadOnlySpan<byte> data)
        {
            if (++Calls == failAt)
                throw new IOException("Partial output failure");
        }
        public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            Write(data.Span);
            return ValueTask.CompletedTask;
        }
        public void Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    }

    [Fact]
    public void IndexCallbackFailureFaultsWriterWithoutRetry()
    {
        var attempts = 0;
        var writer = new Mp4FragmentedWriter(new Mp4FragmentedWriterOptions
        { WriteRandomAccessIndex = true }, segment =>
    {
        if (segment.Kind != Fmp4SegmentKind.Index)
            return;
        attempts++;
        throw new IOException("Index delivery failed");
    });
        var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
        writer.WriteSample(track, new byte[] { 0, 0 }, 0, 0, duration: 1000);
        Assert.Throws<IOException>(() => writer.Complete());
        Assert.Throws<InvalidOperationException>(() => writer.Complete());
        Assert.Throws<InvalidOperationException>(() => writer.FlushFragment());
        writer.Dispose();
        Assert.Equal(1, attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CallbackFailureFaultsWriterWithoutRetryOnDispose(bool initialization)
    {
        var calls = 0;
        var writer = new Mp4FragmentedWriter(new Mp4FragmentedWriterOptions
        { FragmentBoundary = Fmp4FragmentBoundary.Manual }, segment =>
    {
        calls++;
        if (initialization || segment.Kind == Fmp4SegmentKind.Media)
            throw new IOException("Delivery failed");
    });
        var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
        if (initialization)
            Assert.Throws<IOException>(() => writer.WriteSample(track, new byte[] { 0, 0 }, 0, 0));
        else
        {
            writer.WriteSample(track, new byte[] { 0, 0 }, 0, 0);
            Assert.Throws<IOException>(() => writer.FlushFragment());
        }
        Assert.Throws<InvalidOperationException>(() => writer.WriteSample(track, new byte[] { 0, 0 }, 1, 1));
        Assert.Throws<InvalidOperationException>(() => writer.FlushFragment());
        Assert.Throws<InvalidOperationException>(() => writer.Complete());
        var before = calls;
        writer.Dispose();
        Assert.Equal(before, calls);
    }
}
