using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class FragmentSeekDecodeTimeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RepeatedSeekPreservesDecodeTime(bool indexed, bool explicitDecodeTime)
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output),
            new Mp4FragmentedWriterOptions { WriteRandomAccessIndex = indexed }))
        {
            var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            for (var i = 0; i < 4; i++)
            {
                writer.WriteSample(track, new byte[] { 0, 1, (byte)(65 + i) },
                    i * 1000, i * 1000, duration: 1000);
                writer.FlushFragment();
            }
            writer.Complete();
        }
        var bytes = output.ToArray();
        // Keep offsets/indexes valid while removing explicit decode-time anchors.
        for (var i = 0; !explicitDecodeTime && i <= bytes.Length - 4; i++)
            if (bytes.AsSpan(i, 4).SequenceEqual("tfdt"u8))
                "free"u8.CopyTo(bytes.AsSpan(i, 4));
        using var input = new MemoryStream(bytes);
        var reader = new Mp4Reader(input);
        foreach (var target in new[] { 1500, 500, 2500, 1500 })
        {
            await reader.SeekAsync(TimeSpan.FromMilliseconds(target));
            var expected = target / 1000 * 1000;
            var first = await reader.ReadSampleAsync();
            Assert.NotNull(first);
            Assert.Equal(expected, first.Dts);
            var second = await reader.ReadSampleAsync();
            Assert.NotNull(second);
            Assert.Equal(expected + 1000, second.Dts);
        }
    }
}
