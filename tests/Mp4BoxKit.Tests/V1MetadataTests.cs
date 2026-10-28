using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class V1MetadataTests
{
    [Fact]
    public async Task VersionOneMovieTrackAndMediaHeadersAreParsed()
    {
        var file = Combine(
            Box("ftyp", U32(0x69736F6D), U32(0), U32(0x69736F6D)),
            Box("moov", MovieHeaderV1(), TrackV1()));

        using var input = new MemoryStream(file);
        var reader = new Mp4Reader(input);
        var result = await reader.ReadContainerInfoAsync();

        Assert.Equal(ProbeStatus.Complete, result.Status);
        var track = Assert.IsType<VideoTrackInfo>(Assert.Single(reader.Info.Tracks));
        Assert.Equal(new TrackId(7), track.Id);
        Assert.Equal(90_000u, track.TimeScale);
    }

    private static byte[] MovieHeaderV1() => Box("mvhd",
        U32(0x01000000),
        U64(0), U64(0),
        U32(1000),
        U64(0),
        U32(0x00010000),
        U16(0x0100), U16(0),
        U64(0),
        Matrix(),
        U32(0), U32(0), U32(0), U32(0), U32(0), U32(0),
        U32(8));

    private static byte[] TrackV1() => Box("trak",
        Box("tkhd",
            U32(0x01000003),
            U64(0), U64(0),
            U32(7),
            U32(0),
            U64(0),
            U64(0),
            U16(0), U16(0), U16(0), U16(0),
            Matrix(),
            U32(160u << 16), U32(96u << 16)),
        Box("mdia",
            Box("mdhd",
                U32(0x01000000),
                U64(0), U64(0),
                U32(90_000),
                U64(90_000),
                U16(0x55C4), U16(0)),
            Box("hdlr",
                U32(0), U32(0), U32(0x76696465),
                U64(0), U32(0),
                Encoding.UTF8.GetBytes("Video"))));

    private static byte[] Matrix() => Combine(
        U32(0x00010000), U32(0), U32(0),
        U32(0), U32(0x00010000), U32(0),
        U32(0), U32(0), U32(0x40000000));

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

    private static byte[] U16(ushort value)
    {
        var result = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(result, value);
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
