using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class PresentationDelayTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task WriterAndReaderPreserveDelayAndPreroll(bool fragmented, bool fastStart)
    {
        using var output = new MemoryStream();
        using (Mp4Writer writer = fragmented
            ? new Mp4FragmentedWriter(new StreamMp4Output(output))
            : new Mp4FileWriter(new StreamMp4Output(output), new Mp4FileWriterOptions
            { FastStart = fastStart }))
        {
            var track = writer.AddAudio(new AudioTrackOptions(Codec.Aac, 1, 48000,
                CodecConfiguration: TestMedia.Esds,
                Timeline: new TrackTimeline(-1024, PresentationDelay: 240000)));
            writer.WriteSample(track, new byte[] { 1, 2, 3 }, 0, -1024);
            writer.WriteSample(track, new byte[] { 4, 5 }, 1024, 0);
            writer.Complete();
        }
        output.Position = 0;
        var reader = new Mp4Reader(output);
        var first = await reader.ReadSampleAsync();
        Assert.NotNull(first);
        Assert.Equal(240000, first.Pts);
        Assert.Equal(238976, first.Dts);
        Assert.Equal(new byte[] { 1, 2, 3 }, first.Data.ToArray());
        var second = await reader.ReadSampleAsync();
        Assert.NotNull(second);
        Assert.Equal(241024, second.Pts);
        Assert.Equal(240000, second.Dts);
        Assert.Null(await reader.ReadSampleAsync());
    }
}
