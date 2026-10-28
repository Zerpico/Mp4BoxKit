using Mp4BoxKit.Boxes;

namespace Mp4BoxKit.Reader.Metadata;

internal static class EditListParser
{
    public static (long MediaStart, ulong Delay) Parse(ReadOnlySpan<byte> data)
    {
        var boxes = new IsoBoxReader(data);
        while (boxes.TryRead(out var type, out var payload))
        {
            if (type != new FourCc("elst"))
            {
                continue;
            }

            if (payload.Length < 8)
            {
                throw new Mp4FormatException("Truncated elst header.");
            }
            var version = payload[0];
            if (version > 1)
            {
                throw new Mp4FormatException("Unsupported elst version.");
            }
            var count = BigEndian.U32(payload[4..]);
            if (count == 0)
            {
                return (0, 0);
            }
            if (count > 2)
            {
                throw new NotSupportedException("Only a leading empty edit followed by one media edit is supported.");
            }
            var required = version == 1 ? 28 : 20;
            var entrySize = required - 8;
            if (payload.Length < 8 + count * entrySize)
            {
                throw new Mp4FormatException("Truncated elst entry.");
            }
            ulong delay = 0;
            if (count == 2)
            {
                var emptyTime = version == 1
                    ? unchecked((long)BigEndian.U64(payload[16..]))
                    : BigEndian.I32(payload[12..]);
                var emptyRate = version == 1 ? 24 : 16;
                if (emptyTime != -1 || BigEndian.U32(payload[emptyRate..]) != 0x10000)
                    throw new NotSupportedException("Expected a normal-speed leading empty edit.");
                delay = version == 1 ? BigEndian.U64(payload[8..]) : BigEndian.U32(payload[8..]);
                payload = payload[entrySize..];
            }
            var mediaTime = version == 1
                ? unchecked((long)BigEndian.U64(payload[16..])) : BigEndian.I32(payload[12..]);
            var rateOffset = version == 1 ? 24 : 16;
            if (BigEndian.U32(payload[rateOffset..]) != 0x00010000)
                throw new NotSupportedException("Only normal-speed edits are supported.");
            if (mediaTime < 0)
            {
                throw new NotSupportedException(
                    "Empty edits are not supported yet.");
            }
            return (mediaTime, delay);
        }
        return (0, 0);
    }
}
