using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class ReaderLimitsTests
{
    [Fact]
    public async Task FragmentPayloadLimitIsEnforced()
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            writer.WriteSample(track, new byte[] { 0, 1, 65 }, 0, 0, duration: 1000);
            writer.Complete();
        }

        output.Position = 0;
        var reader = new Mp4Reader(output, new Mp4ProbeOptions
        {
            MaxBoxPayloadBytes = 8
        });
        await Assert.ThrowsAsync<Mp4FormatException>(
            async () => await reader.ReadSampleAsync());
    }
}
