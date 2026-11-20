using System.Buffers.Binary;
using Mp4BoxKit.Codecs;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class QuickTimeAudioEntryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ReadsAudioVersionsAndPreservesAacConfiguration(int version)
    {
        var configuration = AacConfiguration.CreateEsds(48000, 2).ToArray();
        var entry = Entry(version, "mp4a", Box("esds", configuration));
        using var input = new MemoryStream(Movie(entry));
        var probe = await Mp4Metadata.ProbeAsync(input);
        var audio = Assert.IsType<AudioTrackInfo>(Assert.Single(probe.Info!.Tracks));
        Assert.Equal(48000, audio.SampleRate);
        Assert.Equal(2, audio.Channels);
        Assert.Equal(16, audio.BitsPerSample);
        Assert.Equal(Codec.Aac, audio.Codec);
        Assert.Equal(configuration, audio.CodecConfiguration.ToArray());
    }

    [Theory]
    [InlineData(48000d, 6u, 24u)]
    [InlineData(96000d, 2u, 32u)]
    public async Task ReadsPcmV2Fields(double rate, uint channels, uint bits)
    {
        var entry = Entry(2, "lpcm", Array.Empty<byte>());
        WriteV2(entry, rate, channels, bits);
        using var input = new MemoryStream(Movie(entry));
        var probe = await Mp4Metadata.ProbeAsync(input);
        var audio = Assert.IsType<AudioTrackInfo>(Assert.Single(probe.Info!.Tracks));
        Assert.Equal((int)rate, audio.SampleRate);
        Assert.Equal((int)channels, audio.Channels);
        Assert.Equal((int)bits, audio.BitsPerSample);
    }

    [Theory]
    [InlineData(double.NaN, 2u)]
    [InlineData(double.PositiveInfinity, 2u)]
    [InlineData(0d, 2u)]
    [InlineData(48000d, 0u)]
    [InlineData(48000d, uint.MaxValue)]
    public async Task RejectsInvalidV2Parameters(double rate, uint channels)
    {
        var entry = Entry(2, "lpcm", Array.Empty<byte>());
        WriteV2(entry, rate, channels, 16);
        using var input = new MemoryStream(Movie(entry));
        await Assert.ThrowsAsync<Mp4FormatException>(async () => await Mp4Metadata.ProbeAsync(input));
    }

    [Fact]
    public async Task RejectsTruncatedV2()
    {
        var entry = Entry(0, "lpcm", Array.Empty<byte>());
        BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(16), 2);
        using var input = new MemoryStream(Movie(entry));
        await Assert.ThrowsAsync<Mp4FormatException>(async () => await Mp4Metadata.ProbeAsync(input));
    }

    private static byte[] Entry(int version, string type, byte[] children)
    {
        var length = version == 2 ? 72 : version == 1 ? 52 : 36;
        var bytes = Box(type, new byte[length - 8].Concat(children).ToArray());
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(16), (ushort)version);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(24), 2);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(26), 16);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(32), 48000u << 16);
        if (version == 2)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(32), 0x10000);
            WriteV2(bytes, 48000, 2, 16);
        }
        return bytes;
    }

    private static void WriteV2(byte[] entry, double rate, uint channels, uint bits)
    {
        BinaryPrimitives.WriteInt64BigEndian(entry.AsSpan(40), BitConverter.DoubleToInt64Bits(rate));
        BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(48), channels);
        BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(56), bits);
    }

    private static byte[] Movie(byte[] entry)
    {
        var tkhd = new byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(tkhd.AsSpan(12), 1);
        var handler = new byte[12];
        "soun"u8.CopyTo(handler.AsSpan(8));
        var stsd = new byte[8].Concat(entry).ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(stsd.AsSpan(4), 1);
        return Box("moov", Box("trak", Box("tkhd", tkhd).Concat(Box("mdia",
            Box("hdlr", handler).Concat(Box("minf", Box("stbl", Box("stsd", stsd)))).ToArray())).ToArray()));
    }

    private static byte[] Box(string type, byte[] payload)
    {
        var bytes = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)bytes.Length);
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4);
        payload.CopyTo(bytes, 8);
        return bytes;
    }
}
