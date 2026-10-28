using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class NonSeekableOutputTests
{
    [Fact]
    public async Task FragmentedWriterWorksWithNonSeekableOutput()
    {
        using var storage = new MemoryStream();
        using var nonSeekable = new NonSeekableStream(storage);
        using (var writer = new Mp4FragmentedWriter(
            new StreamMp4Output(nonSeekable),
            new Mp4FragmentedWriterOptions
            {
                FragmentBoundary = Fmp4FragmentBoundary.Manual,
                WriteSegmentIndex = true
            }))
        {
            var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            writer.WriteSample(track, new byte[] { 0, 1, 65 }, 0, 0, duration: 1000);
            writer.FlushFragment();
            writer.Complete();
        }

        storage.Position = 0;
        var sample = await new Mp4Reader(storage).ReadSampleAsync();
        Assert.NotNull(sample);
        Assert.Equal(new byte[] { 0, 1, 65 }, sample.Data.ToArray());
    }

    [Fact]
    public async Task FragmentedWriterAsyncSinkWorksWithNonSeekableOutput()
    {
        using var storage = new MemoryStream();
        using var nonSeekable = new NonSeekableStream(storage);
        var sink = new RecordingSink();
        await using (var writer = new Mp4FragmentedWriter(
            new StreamMp4Output(nonSeekable),
            new Mp4FragmentedWriterOptions
            {
                FragmentBoundary = Fmp4FragmentBoundary.Manual
            },
            sink))
        {
            var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            await writer.WriteSampleAsync(
                track, new byte[] { 0, 1, 65 }, 0, 0, duration: 1000);
            await writer.FlushFragmentAsync();
            await writer.CompleteAsync();
        }

        Assert.Equal(2, sink.Segments.Count);
        Assert.Equal(Fmp4SegmentKind.Initialization, sink.Segments[0].Kind);
        Assert.Equal(Fmp4SegmentKind.Media, sink.Segments[1].Kind);
        storage.Position = 0;
        Assert.NotNull(await new Mp4Reader(storage).ReadSampleAsync());
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken)
            => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();
        public override void SetLength(long value)
            => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
            => inner.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer)
            => inner.Write(buffer);
        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
            => inner.WriteAsync(buffer, cancellationToken);
    }

    private sealed class RecordingSink : IFmp4SegmentSink
    {
        public List<Fmp4Segment> Segments { get; } = new();

        public ValueTask WriteAsync(
            Fmp4Segment segment,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Segments.Add(segment);
            return ValueTask.CompletedTask;
        }
    }
}
