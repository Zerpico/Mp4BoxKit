using System.Buffers.Binary;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class MalformedChildBoxTests
{
    public static IEnumerable<object[]> InvalidChildren()
    {
        yield return new object[] { new byte[1] };
        yield return new object[] { new byte[7] };
        yield return new object[] { Header(4) };
        yield return new object[] { Header(20) };
        yield return new object[] { Header(1) };
        yield return new object[] { Header(1).Concat(new byte[7]).ToArray() };
        yield return new object[] { Extended(15) };
        yield return new object[] { Extended(100) };
        yield return new object[] { Extended(ulong.MaxValue) };
    }

    [Theory]
    [MemberData(nameof(InvalidChildren))]
    public async Task RejectsMalformedChildInsideCompleteMoov(byte[] child)
    {
        using var input = new MemoryStream(Box("moov", child));
        await Assert.ThrowsAsync<Mp4FormatException>(async () =>
            await Mp4Metadata.ProbeAsync(input));
    }

    [Theory]
    [InlineData("trak")]
    [InlineData("mdia")]
    [InlineData("minf")]
    [InlineData("stbl")]
    [InlineData("mvex")]
    public async Task RejectsMalformedNestedContainer(string container)
    {
        var nested = Box(container, Header(100));
        if (container == "stbl") nested = Box("minf", nested);
        if (container is "stbl" or "minf") nested = Box("mdia", nested);
        if (container is "stbl" or "minf" or "mdia") nested = Box("trak", nested);
        using var input = new MemoryStream(Box("moov", nested));
        await Assert.ThrowsAsync<Mp4FormatException>(async () =>
            await Mp4Metadata.ProbeAsync(input));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptsExactEndAfterUnknownChild(bool extended)
    {
        using var input = new MemoryStream(Box("moov", extended ? Extended(16) : Header(8)));
        Assert.Equal(ProbeStatus.Complete, (await Mp4Metadata.ProbeAsync(input)).Status);
    }

    private static byte[] Header(uint size)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, size);
        "free"u8.CopyTo(bytes.AsSpan(4));
        return bytes;
    }

    private static byte[] Extended(ulong size)
    {
        var bytes = new byte[16];
        Header(1).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8), size);
        return bytes;
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
