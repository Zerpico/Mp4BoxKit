using Mp4BoxKit;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class PrimitiveTests
{
    [Fact]
    public void FourCcRoundTrips()
        => Assert.Equal("moov", new FourCc("moov").ToString());

    [Fact]
    public async Task ProbeReadsFtypFromNonSeekableStream()
    {
        var bytes = new byte[16];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes, 16);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), new FourCc("ftyp").Value);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), new FourCc("isom").Value);
        await using var stream = new ChunkedStream(bytes, 3);
        var result = await Mp4Metadata.ProbeAsync(stream);
        Assert.Equal(ProbeStatus.EndOfStream, result.Status);
        Assert.NotNull(result.Info);
        Assert.Equal("isom", result.Info!.MajorBrand.ToString());
    }

    private sealed class ChunkedStream(byte[] data, int chunk) : Stream
    {
        private int _offset;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;

        public override long Position
        {
            get => _offset;
            set => throw new NotSupportedException();
        }

        public override int Read(Span<byte> buffer)
        {
            var count = Math.Min(
                Math.Min(chunk, buffer.Length),
                data.Length - _offset);
            data.AsSpan(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
            => new(Read(buffer.Span));

        public override int Read(byte[] buffer, int offset, int count)
            => Read(buffer.AsSpan(offset, count));

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();
    }
}
