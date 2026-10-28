namespace Mp4BoxKit.Boxes;

/// <summary>
/// Bounded reader for a sequence of ISO-BMFF child boxes.
/// </summary>
internal ref struct IsoBoxReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _offset;

    public IsoBoxReader(ReadOnlySpan<byte> data, int offset = 0)
    {
        _data = data;
        _offset = offset;
    }

    public int Position => _offset;

    public bool TryRead(
        out FourCc type,
        out ReadOnlySpan<byte> payload)
    {
        type = default;
        payload = default;

        if (_offset > _data.Length - 8)
        {
            return false;
        }

        var size = BigEndian.U32(_data[_offset..]);
        type = new FourCc(BigEndian.U32(_data[(_offset + 4)..]));
        var headerSize = 8;
        ulong actualSize = size;

        if (size == 1)
        {
            if (_offset > _data.Length - 16)
            {
                return false;
            }

            headerSize = 16;
            actualSize = BigEndian.U64(_data[(_offset + 8)..]);
        }

        if (actualSize < (ulong)headerSize ||
            actualSize > (ulong)(_data.Length - _offset) ||
            actualSize > int.MaxValue)
        {
            return false;
        }

        var payloadLength = checked((int)actualSize - headerSize);
        payload = _data.Slice(_offset + headerSize, payloadLength);
        _offset += checked((int)actualSize);
        return true;
    }
}
