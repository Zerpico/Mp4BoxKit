using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class VariableFrameRateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InfersVariableDurationsAcrossKeyframeBoundaries(bool asynchronous)
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output),
            new Mp4FragmentedWriterOptions { FragmentBoundary = Fmp4FragmentBoundary.EveryKeyFrame }))
        {
            var track = writer.AddVideo(new VideoTrackOptions(Codec.Jpeg, 640, 480, 1000));
            foreach (var timestamp in new long[] { 0, 200, 400, 440, 480, 680 })
            {
                if (asynchronous)
                    await writer.WriteSampleAsync(track, new byte[] { 0xff, 0xd8, 0xff, 0xd9 }, timestamp, timestamp, SampleFlags.KeyFrame);
                else
                    writer.WriteSample(track, new byte[] { 0xff, 0xd8, 0xff, 0xd9 }, timestamp, timestamp, SampleFlags.KeyFrame);
            }
            await writer.CompleteAsync();
        }
        output.Position = 0;
        var reader = new Mp4Reader(output);
        var samples = new List<Mp4Sample>();
        while (await reader.ReadSampleAsync() is { } sample) samples.Add(sample);
        Assert.Equal(new long[] { 0, 200, 400, 440, 480, 680 }, samples.Select(s => s.Dts));
        Assert.Equal(new[] { 200, 200, 40, 40, 200 }, samples.Take(5).Select(s => s.Duration));
    }
}
