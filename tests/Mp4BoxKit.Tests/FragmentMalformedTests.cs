using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class FragmentMalformedTests
{
    [Fact]
    public async Task TruncatedTrunSampleFieldThrows()
    {
        using var initOutput = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(
            new StreamMp4Output(initOutput)))
        {
            writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            _ = writer.InitializationSegment;
        }

        var tfhd = Box("tfhd", U32(0x020000), U32(1));
        var tfdt = Box("tfdt", U32(0x01000000), U64(0));
        // sample_size is declared but only duration bytes follow.
        var trun = Box("trun", U32(0x00000301), U32(1), U32(0), U32(1000));
        var moof = Box("moof",
            Box("mfhd", U32(0), U32(1)),
            Box("traf", tfhd, tfdt, trun));
        var file = Combine(
            initOutput.ToArray(),
            moof,
            Box("mdat", new byte[] { 0, 1, 65 }));

        using var input = new MemoryStream(file);
        var reader = new Mp4Reader(input);
        await Assert.ThrowsAsync<Mp4FormatException>(
            async () => await reader.ReadSampleAsync());
    }

    private static byte[] Box(string type, params byte[][] parts)
    {
        var length = parts.Sum(part => part.Length);
        var result = new byte[8 + length];
        BinaryPrimitives.WriteUInt32BigEndian(result, checked((uint)result.Length));
        Encoding.ASCII.GetBytes(type).CopyTo(result, 4);
        var offset = 8;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }
        return result;
    }

    private static byte[] Combine(params byte[][] parts)
    {
        var result = new byte[parts.Sum(part => part.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }
        return result;
    }

    private static byte[] U32(uint value)
    {
        var result = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(result, value);
        return result;
    }

    private static byte[] U64(ulong value)
    {
        var result = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(result, value);
        return result;
    }
}
