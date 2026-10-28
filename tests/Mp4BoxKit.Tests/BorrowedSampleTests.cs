using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class BorrowedSampleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BorrowedSampleIsConsumedAtFlush(bool deliver)
    {
        var original = TestMedia.VideoSample();
        var payload = (byte[])original.Clone();
        var segments = new List<Fmp4Segment>();
        using var output = new MemoryStream();
        var options = new Mp4FragmentedWriterOptions
        {
            FragmentBoundary = Fmp4FragmentBoundary.Manual
        };

        using (Mp4FragmentedWriter writer = deliver
            ? new Mp4FragmentedWriter(new StreamMp4Output(output), options, segments.Add)
            : new Mp4FragmentedWriter(new StreamMp4Output(output), options))
        {
            var track = writer.AddVideo(new VideoTrackOptions(
                Codec.H264, 16, 16, CodecConfiguration: TestMedia.AvcC));
            writer.WriteSampleBorrowed(track, payload, 0, 0, SampleFlags.KeyFrame);
            writer.FlushFragment();
            payload[5] = 0xFF; // caller may reuse the buffer after flush
            writer.Complete();
        }

        output.Position = 0;
        var sample = await new Mp4Reader(output).ReadSampleAsync();
        Assert.NotNull(sample);
        Assert.Equal(original, sample.Data.ToArray());
    }

    [Fact]
    public async Task BorrowedAsyncSampleIsConsumedAtFlush()
    {
        var original = TestMedia.VideoSample();
        var payload = (byte[])original.Clone();
        using var output = new MemoryStream();

        await using (var writer = new Mp4FragmentedWriter(
            new StreamMp4Output(output),
            new Mp4FragmentedWriterOptions
            {
                FragmentBoundary = Fmp4FragmentBoundary.Manual
            }))
        {
            var track = writer.AddVideo(new VideoTrackOptions(
                Codec.H264, 16, 16, CodecConfiguration: TestMedia.AvcC));
            await writer.WriteSampleBorrowedAsync(
                track, payload, 0, 0, SampleFlags.KeyFrame);
            await writer.FlushFragmentAsync();
            payload[5] = 0xFF;
            await writer.CompleteAsync();
        }

        output.Position = 0;
        var sample = await new Mp4Reader(output).ReadSampleAsync();
        Assert.NotNull(sample);
        Assert.Equal(original, sample.Data.ToArray());
    }

    [Fact]
    public async Task ClassicBorrowedSampleIsWrittenImmediately()
    {
        var original = TestMedia.VideoSample();
        var payload = (byte[])original.Clone();
        using var output = new MemoryStream();

        using (Mp4Writer writer = new Mp4FileWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddVideo(new VideoTrackOptions(
                Codec.H264, 16, 16, CodecConfiguration: TestMedia.AvcC));
            writer.WriteSampleBorrowed(track, payload, 0, 0, SampleFlags.KeyFrame);
            payload[5] = 0xFF;
            writer.Complete();
        }

        output.Position = 0;
        var sample = await new Mp4Reader(output).ReadSampleAsync();
        Assert.NotNull(sample);
        Assert.Equal(original, sample.Data.ToArray());
    }
}
