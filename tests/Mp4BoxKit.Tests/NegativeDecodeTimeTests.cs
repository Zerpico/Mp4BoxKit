using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class NegativeDecodeTimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NegativeDtsIsRejectedBeforeWriting(bool fragmented)
    {
        using var output = new MemoryStream();
        using Mp4Writer writer = fragmented
            ? new Mp4FragmentedWriter(new StreamMp4Output(output))
            : new Mp4FileWriter(new StreamMp4Output(output));
        var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            writer.WriteSample(track, new byte[] { 0, 1, 65 }, 0, -1000));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await writer.WriteSampleAsync(track, new byte[] { 0, 1, 65 }, 0, -1000));
        Assert.Equal(0, output.Length);
        writer.WriteSample(track, new byte[] { 0, 1, 65 }, 0, 0);
        writer.Complete();
        output.Position = 0;
        var reader = new Mp4Reader(output);
        var sample = await reader.ReadSampleAsync();
        Assert.NotNull(sample);
        Assert.Equal(0, sample.Dts);
        Assert.Null(await reader.ReadSampleAsync());
    }
}
