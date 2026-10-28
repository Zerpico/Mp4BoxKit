namespace Mp4BoxKit;

public interface IMp4Output
{
    bool CanSeek { get; }
    long Position { get; }
    /// <summary>Writes all data. The implementation must not retain the buffer.</summary>
    void Write(ReadOnlySpan<byte> data);
    /// <summary>Writes all data. The implementation must not retain the buffer
    /// after the returned task completes; the writer may reuse pooled storage.</summary>
    ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
    void Seek(long offset, SeekOrigin origin);
}

/// <summary>
/// Asynchronous destination for fragmented MP4 parts. A sink can write
/// initialization, media and index parts directly to a network or other
/// asynchronous transport without an intermediate file.
/// </summary>
public interface IFmp4SegmentSink
{
    ValueTask WriteAsync(Fmp4Segment segment, CancellationToken cancellationToken = default);
}

public sealed class StreamMp4Output(Stream stream) : IMp4Output
{
    private readonly Stream _stream =
        stream ?? throw new ArgumentNullException(nameof(stream));

    public bool CanSeek => _stream.CanSeek;
    public long Position => _stream.Position;

    public void Write(ReadOnlySpan<byte> data) => _stream.Write(data);

    public ValueTask WriteAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
        => _stream.WriteAsync(data, cancellationToken);

    public void Seek(long offset, SeekOrigin origin)
        => _stream.Seek(offset, origin);
}

internal sealed class DiscardingMp4Output : IMp4Output
{
    public bool CanSeek => false;
    public long Position { get; private set; }

    public void Write(ReadOnlySpan<byte> data)
    {
        Position += data.Length;
    }

    public ValueTask WriteAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Position += data.Length;
        return ValueTask.CompletedTask;
    }

    public void Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }
}

internal sealed class InputBuffer(Stream stream)
{
    private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    public long Position { get; private set; }

    public void ResetPosition(long position = 0) => Position = position;
    public bool CanSeek => _stream.CanSeek;
    public long Length => _stream.Length;
    public void Seek(long position)
    {
        _stream.Seek(position, SeekOrigin.Begin);
        Position = position;
    }

    public async ValueTask<int> ReadAtLeastAsync(Memory<byte> destination, int minimum, CancellationToken ct)
    {
        var read = 0;
        while (read < minimum)
        {
            var n = await ReadAsync(destination[read..], ct).ConfigureAwait(false);
            if (n == 0)
            {
                break;
            }
            read += n;
        }
        return read;
    }
    public int ReadAtLeast(Span<byte> destination, int minimum)
    {
        var read = 0;
        while (read < minimum)
        {
            var n = Read(destination[read..]);
            if (n == 0)
            {
                break;
            }
            read += n;
        }
        return read;
    }
    public async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken ct)
    {
        var n = await _stream.ReadAsync(destination, ct).ConfigureAwait(false);
        Position += n;
        return n;
    }
    public int Read(Span<byte> destination)
    {
        var n = _stream.Read(destination);
        Position += n;
        return n;
    }
}
