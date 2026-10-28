using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class AsyncSinkTests
{
    [Fact]
    public async Task AsyncSinkReceivesInitializationMediaAndIndex()
    {
        var sink = new RecordingSink();
        using var output = new MemoryStream();
        await using (var writer = new Mp4FragmentedWriter(
            new StreamMp4Output(output),
            new Mp4FragmentedWriterOptions
            {
                FragmentBoundary = Fmp4FragmentBoundary.Manual,
                WriteSegmentIndex = true,
                WriteRandomAccessIndex = true
            },
            sink))
        {
            var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            await writer.WriteSampleAsync(
                track, new byte[] { 0, 1, 65 }, 0, 0, duration: 1000);
            await writer.FlushFragmentAsync();
            await writer.CompleteAsync();
        }

        Assert.Equal(
            new[] { Fmp4SegmentKind.Initialization, Fmp4SegmentKind.Media, Fmp4SegmentKind.Index },
            sink.Segments.Select(segment => segment.Kind));
        Assert.Equal(output.ToArray(),
            sink.Segments.SelectMany(segment => segment.Data.ToArray()).ToArray());
        Assert.Equal(0, sink.Segments[0].Offset);
        Assert.Equal(sink.Segments[0].Data.Length, sink.Segments[1].Offset);
    }

    [Fact]
    public async Task AsyncSinkFailureFaultsWriter()
    {
        using var output = new MemoryStream();
        var writer = new Mp4FragmentedWriter(
            new StreamMp4Output(output),
            new Mp4FragmentedWriterOptions
            {
                FragmentBoundary = Fmp4FragmentBoundary.Manual
            },
            new ThrowingSink());
        var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));

        Assert.Throws<IOException>(() =>
            writer.WriteSample(track, new byte[] { 0, 1, 65 }, 0, 0, duration: 1000));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await writer.WriteSampleAsync(track, new byte[] { 0, 1, 65 }, 1000, 1000));
        await writer.DisposeAsync();
    }

    private sealed class RecordingSink : IFmp4SegmentSink
    {
        public List<Fmp4Segment> Segments { get; } = new();

        public async ValueTask WriteAsync(
            Fmp4Segment segment,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            Segments.Add(segment);
        }
    }

    private sealed class ThrowingSink : IFmp4SegmentSink
    {
        public ValueTask WriteAsync(
            Fmp4Segment segment,
            CancellationToken cancellationToken = default)
            => throw new IOException("Sink failure");
    }
}
