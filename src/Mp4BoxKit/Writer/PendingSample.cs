using System.Buffers;

namespace Mp4BoxKit.Writer;

internal sealed class PendingSample
{
    private readonly byte[]? _pooledBuffer;
    private readonly ReadOnlyMemory<byte> _borrowed;

    public readonly int Length;
    public readonly long Pts;
    public readonly long Dts;
    public readonly SampleFlags Flags;
    public int Duration;
    public bool HasExplicitDuration;

    public PendingSample(
        byte[] buffer,
        int length,
        long pts,
        long dts,
        SampleFlags flags)
    {
        _pooledBuffer = buffer;
        _borrowed = default;
        Length = length;
        Pts = pts;
        Dts = dts;
        Flags = flags;
    }

    public PendingSample(
        ReadOnlyMemory<byte> borrowed,
        long pts,
        long dts,
        SampleFlags flags)
    {
        _pooledBuffer = null;
        _borrowed = borrowed;
        Length = borrowed.Length;
        Pts = pts;
        Dts = dts;
        Flags = flags;
    }

    public ReadOnlySpan<byte> Data => _pooledBuffer is null
        ? _borrowed.Span
        : _pooledBuffer.AsSpan(0, Length);

    public ReadOnlyMemory<byte> Memory => _pooledBuffer is null
        ? _borrowed
        : _pooledBuffer.AsMemory(0, Length);

    public void Return()
    {
        if (_pooledBuffer is not null)
            ArrayPool<byte>.Shared.Return(_pooledBuffer);
    }
}
