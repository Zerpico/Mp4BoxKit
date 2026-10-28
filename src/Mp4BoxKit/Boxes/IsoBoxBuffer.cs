using System.Buffers.Binary;

namespace Mp4BoxKit.Boxes;

internal sealed class IsoBoxBuffer
{
    private byte[] _buffer = new byte[256];
    private int _length;

    private Span<byte> Reserve(int count)
    {
        var required = checked(_length + count);
        if (required > _buffer.Length)
        {
            var capacity = (int)Math.Min(int.MaxValue,
                Math.Max((long)required, (long)_buffer.Length * 2));
            Array.Resize(ref _buffer, capacity);
        }
        var result = _buffer.AsSpan(_length, count);
        _length = required;
        return result;
    }

    public void WriteUInt32(uint value)
    {
        var span = Reserve(4);
        BinaryPrimitives.WriteUInt32BigEndian(span, value);
    }

    public void WriteUInt64(ulong value)
    {
        var span = Reserve(8);
        BinaryPrimitives.WriteUInt64BigEndian(span, value);
    }

    public void WriteUInt16(ushort value)
    {
        var span = Reserve(2);
        BinaryPrimitives.WriteUInt16BigEndian(span, value);
    }

    public void WriteInt32(int value) => WriteUInt32(unchecked((uint)value));

    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        value.CopyTo(Reserve(value.Length));
    }

    public int BeginBox(FourCc type)
    {
        var position = _length;
        WriteUInt32(0);
        WriteUInt32(type.Value);
        return position;
    }

    public void EndBox(int position)
    {
        if (position < 0 || position > _length - 8)
            throw new ArgumentOutOfRangeException(nameof(position));
        var size = checked((uint)(_length - position));
        BinaryPrimitives.WriteUInt32BigEndian(_buffer.AsSpan(position, 4), size);
    }

    public byte[] ToArray() => _buffer.AsSpan(0, _length).ToArray();
}
