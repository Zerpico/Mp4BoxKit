using System.Buffers.Binary;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class FragmentCompositionOffsetTests
{
    [Theory]
    [InlineData(0, 2147483648L)]
    [InlineData(1, -2147483648L)]
    public async Task CompositionOffsetUsesTrunVersion(int version, long expectedPts)
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            writer.WriteSample(track, new byte[] { 0, 1, 65 }, 1, 0);
            writer.Complete();
        }
        var bytes = output.ToArray();
        var typeOffset = bytes.AsSpan().IndexOf("trun"u8);
        Assert.True(typeOffset >= 4);
        var payload = typeOffset + 4;
        bytes[payload] = (byte)version;
        var flags = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(payload)) & 0xffffff;
        Assert.NotEqual(0u, flags & 0x800);
        var offset = payload + 8;
        if ((flags & 1) != 0)
            offset += 4;
        if ((flags & 4) != 0)
            offset += 4;
        if ((flags & 0x100) != 0)
            offset += 4;
        if ((flags & 0x200) != 0)
            offset += 4;
        if ((flags & 0x400) != 0)
            offset += 4;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), 0x80000000);
        using var input = new MemoryStream(bytes);
        var reader = new Mp4Reader(input);
        var sample = await reader.ReadSampleAsync();
        Assert.NotNull(sample);
        Assert.Equal(0, sample.Dts);
        Assert.Equal(expectedPts, sample.Pts);
    }
}
