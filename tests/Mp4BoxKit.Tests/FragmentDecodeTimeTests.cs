using System.Buffers.Binary;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class FragmentDecodeTimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadsBothDecodeTimeVersions(bool versionOne)
    {
        var bytes = CreateFragment();
        if (!versionOne)
            bytes = ConvertToVersionZero(bytes);
        using var input = new MemoryStream(bytes);
        var reader = new Mp4Reader(input);
        var sample = await reader.ReadSampleAsync();
        Assert.NotNull(sample);
        Assert.Equal(5000, sample.Dts);
        Assert.Equal(5000, sample.Pts);
        Assert.Equal(new byte[] { 0, 1, 65 }, sample.Data.ToArray());
    }

    [Fact]
    public async Task RejectsUnsupportedDecodeTimeVersion()
    {
        var bytes = CreateFragment();
        var tfdt = bytes.AsSpan().IndexOf("tfdt"u8);
        bytes[tfdt + 4] = 2;
        using var input = new MemoryStream(bytes);
        var reader = new Mp4Reader(input);
        await Assert.ThrowsAsync<Mp4FormatException>(async () => await reader.ReadSampleAsync());
    }

    private static byte[] CreateFragment()
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            writer.WriteSample(track, new byte[] { 0, 1, 65 }, 5000, 5000);
            writer.Complete();
        }
        return output.ToArray();
    }

    private static byte[] ConvertToVersionZero(byte[] bytes)
    {
        var tfdt = bytes.AsSpan().IndexOf("tfdt"u8);
        var moof = bytes.AsSpan().IndexOf("moof"u8);
        var traf = bytes.AsSpan(moof).IndexOf("traf"u8) + moof;
        var trun = bytes.AsSpan(tfdt).IndexOf("trun"u8) + tfdt;
        foreach (var type in new[] { moof, traf })
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(type - 4),
                BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(type - 4)) - 4);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(trun + 12),
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(trun + 12)) - 4);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(tfdt - 4), 16);
        bytes[tfdt + 4] = 0;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(tfdt + 8), 5000);
        return bytes.AsSpan(0, tfdt + 12).ToArray()
            .Concat(bytes.AsSpan(tfdt + 16).ToArray()).ToArray();
    }
}
