namespace Mp4BoxKit.Reader;

internal sealed class ContainerInput(InputBuffer input)
{
    private readonly byte[] _header = new byte[16];
    private readonly byte[] _skipBuffer = new byte[64 * 1024];
    public long Position => input.Position;
    public bool CanSeek => input.CanSeek;
    public long Length => input.Length;
    public void Seek(long position) => input.Seek(position);

    public async ValueTask<BoxHeader?> ReadHeaderAsync(CancellationToken cancellationToken)
    {
        var read = await input.ReadAtLeastAsync(_header.AsMemory(0, 8), 8,
            cancellationToken).ConfigureAwait(false);
        if (read == 0)
            return null;
        if (read < 8)
            return new BoxHeader(0, default, 0);
        var size = BigEndian.U32(_header);
        var type = new FourCc(BigEndian.U32(_header.AsSpan(4)));
        if (size != 1)
            return new BoxHeader(size, type, 8);
        if (await input.ReadAtLeastAsync(_header.AsMemory(8, 8), 8,
                cancellationToken).ConfigureAwait(false) < 8)
            return new BoxHeader(0, default, 0);
        return new BoxHeader(BigEndian.U64(_header.AsSpan(8)), type, 16);
    }

    public async ValueTask<byte[]> ReadPayloadAsync(ulong size,
        CancellationToken cancellationToken)
    {
        if (size > int.MaxValue)
            throw new Mp4FormatException(
                "Box payload is too large for the current streaming reader.");
        var payload = new byte[(int)size];
        if (await input.ReadAtLeastAsync(payload, payload.Length,
                cancellationToken).ConfigureAwait(false) != payload.Length)
            throw new EndOfStreamException();
        return payload;
    }

    public async ValueTask SkipAsync(ulong bytes, CancellationToken cancellationToken)
    {
        while (bytes != 0)
        {
            var requested = (int)Math.Min((ulong)_skipBuffer.Length, bytes);
            var read = await input.ReadAsync(_skipBuffer.AsMemory(0, requested),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return;
            bytes -= (ulong)read;
        }
    }
}
