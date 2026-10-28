using System.Buffers.Binary;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class FastStartTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FastStartPlacesMovieBeforeMediaAndPreservesInterleavedSamples(bool fastStart)
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FileWriter(new StreamMp4Output(output),
            new Mp4FileWriterOptions { FastStart = fastStart }))
        {
            var a = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            var b = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            for (var i = 0; i < 6; i++)
                writer.WriteSample(i % 2 == 0 ? a : b, new byte[] { 0, 1, (byte)(65 + i) },
                    i / 2 * 1000, i / 2 * 1000);
            writer.Complete();
        }
        var bytes = output.ToArray();
        var types = new List<string>();
        for (var offset = 0; offset < bytes.Length;)
        {
            types.Add(System.Text.Encoding.ASCII.GetString(bytes, offset + 4, 4));
            offset += checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset)));
        }
        Assert.Equal(fastStart ? new[] { "ftyp", "moov", "mdat" } : new[] { "ftyp", "mdat", "moov" }, types);
        output.Position = 0;
        var reader = new Mp4Reader(output);
        for (var i = 0; i < 6; i++)
        {
            var sample = await reader.ReadSampleAsync();
            Assert.NotNull(sample);
            Assert.Equal(new byte[] { 0, 1, (byte)(65 + i) }, sample.Data.ToArray());
            Assert.Equal((uint)(i % 2 + 1), sample.Track.Value);
            Assert.Equal(i / 2 * 1000L, sample.Dts);
        }
        Assert.Null(await reader.ReadSampleAsync());
    }
}
