using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class ClassicFailureTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(false, 4)]
    [InlineData(false, 5)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    [InlineData(true, 4)]
    public void OutputFailureFaultsWriterWithoutRetry(bool fastStart, int failAt)
    {
        var output = new FailingOutput(failAt);
        var writer = new Mp4FileWriter(output, new Mp4FileWriterOptions
        { FastStart = fastStart });
        var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
        Assert.Throws<IOException>(() =>
        {
            writer.WriteSample(track, new byte[] { 0, 0 }, 0, 0, duration: 1000);
            writer.Complete();
        });
        Assert.Throws<InvalidOperationException>(() => writer.Complete());
        Assert.Throws<InvalidOperationException>(() => writer.WriteSample(track, new byte[] { 0, 0 }, 1000, 1000));
        writer.Dispose();
        Assert.Equal(failAt, output.Calls);
    }

    private sealed class FailingOutput(int failAt) : IMp4Output
    {
        public int Calls;
        public bool CanSeek => true;
        public long Position { get; private set; }
        public void Write(ReadOnlySpan<byte> data)
        {
            Position += data.Length;
            if (++Calls == failAt)
                throw new IOException("Partial write");
        }
        public void Seek(long offset, SeekOrigin origin) => Position = offset;
        public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            Write(data.Span);
            return ValueTask.CompletedTask;
        }
    }
}
