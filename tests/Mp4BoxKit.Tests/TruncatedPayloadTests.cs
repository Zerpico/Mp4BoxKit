using System.Buffers.Binary;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class TruncatedPayloadTests
{
    [Theory]
    [InlineData("moov", true)]
    [InlineData("moov", false)]
    [InlineData("ftyp", true)]
    [InlineData("ftyp", false)]
    [InlineData("free", true)]
    [InlineData("free", false)]
    public async Task RejectsTruncatedPayload(string type, bool seekable)
    {
        using var input = new TestStream(Header(type, 1200), seekable);
        var reader = new Mp4Reader(input);
        await Assert.ThrowsAsync<Mp4FormatException>(async () =>
            await reader.ReadContainerInfoAsync());
        Assert.Empty(reader.Info.Tracks);
        // A failed moov must not make subsequent probing report Complete.
        Assert.Equal(ProbeStatus.EndOfStream,
            (await reader.ReadContainerInfoAsync()).Status);
    }

    [Fact]
    public async Task RejectsHugeDeclaredMoovBeforeReadingPayload()
    {
        using var input = new TestStream(Header("moov", 0x7f000000), true);
        var reader = new Mp4Reader(input, new Mp4ProbeOptions
        {
            MaxMetadataBytes = long.MaxValue
        });
        await Assert.ThrowsAsync<Mp4FormatException>(async () =>
            await reader.ReadContainerInfoAsync());
        Assert.Equal(1, input.ReadCalls);
    }

    [Fact]
    public async Task RejectsPayloadLargerThanArrayRange()
    {
        var header = new byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(header, 1);
        "moov"u8.CopyTo(header.AsSpan(4));
        BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(8), (ulong)int.MaxValue + 17);
        using var input = new TestStream(header, false);
        var reader = new Mp4Reader(input, new Mp4ProbeOptions { MaxMetadataBytes = long.MaxValue });
        await Assert.ThrowsAsync<Mp4FormatException>(async () =>
            await reader.ReadContainerInfoAsync());
    }

    private static byte[] Header(string type, uint size)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, size);
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4);
        return bytes;
    }

    private sealed class TestStream(byte[] bytes, bool seekable) : MemoryStream(bytes)
    {
        public int ReadCalls { get; private set; }
        public override bool CanSeek => seekable;
        public override long Length => seekable ? base.Length : throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return base.ReadAsync(buffer, cancellationToken);
        }
    }
}
