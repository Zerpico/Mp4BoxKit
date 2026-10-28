using System.Buffers.Binary;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class ClassicChunkLayoutTests
{
    [Fact]
    public void EachChunkContainsOneSample()
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FileWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            for (var i = 0; i < 3; i++)
                writer.WriteSample(track, new byte[] { 0, 1, 65 }, i * 1000, i * 1000);
            writer.Complete();
        }
        var bytes = output.ToArray();
        var stsc = bytes.AsSpan().IndexOf("stsc"u8);
        var stco = bytes.AsSpan().IndexOf("stco"u8);
        Assert.True(stsc >= 4);
        Assert.True(stco >= 4);
        Assert.Equal(1u, Read(stsc + 8)); // entry count
        Assert.Equal(1u, Read(stsc + 12)); // first chunk
        Assert.Equal(1u, Read(stsc + 16)); // samples per chunk
        Assert.Equal(1u, Read(stsc + 20)); // sample description index
        Assert.Equal(3u, Read(stco + 8)); // one chunk offset per sample

        uint Read(int offset) => BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset));
    }
}
