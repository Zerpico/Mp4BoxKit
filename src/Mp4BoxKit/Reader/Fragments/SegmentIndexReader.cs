namespace Mp4BoxKit.Reader.Fragments;

/// <summary>Reads direct, independently decodable sidx references without reading media payload.</summary>
internal sealed class SegmentIndexReader(ContainerInput input)
{
    private readonly List<Entry> _entries = new();
    private bool _loaded;
    private readonly record struct Entry(uint Track, decimal Time, long Offset);

    public async ValueTask<long?> FindAsync(
        uint track,
        uint timeScale,
        double seconds,
        CancellationToken cancellationToken)
    {
        if (!_loaded)
        {
            var entries = new List<Entry>();
            _randomAccess.Clear();
            var saved = input.Position;
            try
            {
                input.Seek(0);
                while (input.Position < input.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var start = input.Position;
                    var header = await input.ReadHeaderAsync(cancellationToken).ConfigureAwait(false);
                    if (header is null)
                    {
                        break;
                    }
                    var box = header.Value;
                    if (box.Size == 0)
                    {
                        break;
                    }
                    if (box.Size < (ulong)box.HeaderSize || box.Size > (ulong)(input.Length - start))
                        throw new Mp4FormatException("Invalid box while scanning fragment indexes.");
                    var end = checked(start + (long)box.Size);
                    if (box.Type == BoxTypes.Mfra)
                    {
                        if (box.PayloadSize > 64 * 1024 * 1024)
                            throw new Mp4FormatException("Oversized mfra.");
                        var payload = await input.ReadPayloadAsync(
                                box.PayloadSize,
                                cancellationToken)
                            .ConfigureAwait(false);
                        ParseMfra(payload, input.Length, entries);
                    }
                    else if (box.Type == BoxTypes.Sidx)
                    {
                        // sidx has a 16-bit reference count; larger boxes are not useful here.
                        if (box.PayloadSize > 1024 * 1024)
                            throw new Mp4FormatException("Oversized sidx.");
                        var payload = await input.ReadPayloadAsync(
                                box.PayloadSize,
                                cancellationToken)
                            .ConfigureAwait(false);
                        Parse(payload, end, input.Length, entries);
                    }
                    input.Seek(end);
                }
                _entries.AddRange(entries);
                _loaded = true;
            }
            finally { input.Seek(saved); }
        }
        Entry? best = null;
        foreach (var entry in _entries)
            if (entry.Track == track && entry.Time <= (decimal)seconds &&
                (best is null || entry.Time > best.Value.Time))
                best = entry;
        foreach (var entry in _randomAccess)
        {
            var time = (decimal)entry.Time / Math.Max(1, timeScale);
            if (entry.Track == track && time <= (decimal)seconds &&
                (best is null || time > best.Value.Time))
                best = new Entry(track, time, entry.Offset);
        }
        return best?.Offset;
    }

    private static void Parse(ReadOnlySpan<byte> data, long end, long length, List<Entry> entries)
    {
        if (data.Length < 24 || data[0] > 1)
            throw new Mp4FormatException("Invalid sidx header.");
        var version = data[0];
        var track = BigEndian.U32(data[4..]);
        var scale = BigEndian.U32(data[8..]);
        if (scale == 0)
        {
            throw new Mp4FormatException("Zero sidx timescale.");
        }
        var cursor = version == 0 ? 20 : 28;
        if (data.Length < cursor + 4)
        {
            throw new Mp4FormatException("Truncated sidx.");
        }
        var time = version == 0 ? BigEndian.U32(data[12..]) : BigEndian.U64(data[12..]);
        var firstOffset = version == 0 ? BigEndian.U32(data[16..]) : BigEndian.U64(data[20..]);
        if (firstOffset > (ulong)(length - end))
        {
            throw new Mp4FormatException("sidx offset outside input.");
        }
        var offset = end + (long)firstOffset;
        var count = BigEndian.U16(data[(cursor + 2)..]);
        cursor += 4;
        if (count > (data.Length - cursor) / 12)
        {
            throw new Mp4FormatException("Truncated sidx references.");
        }
        for (var i = 0; i < count; i++, cursor += 12)
        {
            var reference = BigEndian.U32(data[cursor..]);
            var size = reference & 0x7fffffff;
            var duration = BigEndian.U32(data[(cursor + 4)..]);
            var sap = BigEndian.U32(data[(cursor + 8)..]);
            if (size > length - offset)
            {
                throw new Mp4FormatException(
                    "sidx reference outside input.");
            }
            // Hierarchical references and nonzero SAP deltas need more traversal/preroll.
            if ((reference & 0x80000000) == 0 && size != 0 && sap == 0x90000000)
                entries.Add(new Entry(track, (decimal)time / scale, offset));
            offset += size;
            time = checked(time + duration);
        }
    }

    private void ParseMfra(ReadOnlySpan<byte> data, long length, List<Entry> entries)
    {
        while (!data.IsEmpty)
        {
            if (data.Length < 8)
            {
                throw new Mp4FormatException("Truncated mfra child.");
            }

            var size = BigEndian.U32(data);
            if (size < 8 || size > data.Length)
            {
                throw new Mp4FormatException("Invalid mfra child size.");
            }
            if (BigEndian.U32(data[4..]) == BoxTypes.Tfra.Value)
                ValidateTfra(data.Slice(8, (int)size - 8), length, _randomAccess);
            data = data[(int)size..];
        }
    }

    // tfra time is in track ticks; converted once track timescales are available.
    private readonly List<(uint Track, ulong Time, long Offset)> _randomAccess = new();

    private static void ValidateTfra(ReadOnlySpan<byte> data, long length,
        List<(uint Track, ulong Time, long Offset)>? result)
    {
        if (data.Length < 16 || data[0] > 1)
        {
            throw new Mp4FormatException("Invalid tfra header.");
        }

        var wide = data[0] == 1;
        var track = BigEndian.U32(data[4..]);
        var sizes = BigEndian.U32(data[8..]);
        var numberBytes = (int)(((sizes >> 4) & 3) + ((sizes >> 2) & 3) + (sizes & 3) + 3);
        var count = BigEndian.U32(data[12..]);
        var stride = (wide ? 16 : 8) + numberBytes;
        if (count > (data.Length - 16) / stride)
        {
            throw new Mp4FormatException("Truncated tfra entries.");
        }
        for (var i = 0; i < count; i++)
        {
            var item = data[(16 + i * stride)..];
            var time = wide ? BigEndian.U64(item) : BigEndian.U32(item);
            var offset = wide ? BigEndian.U64(item[8..]) : BigEndian.U32(item[4..]);
            if (offset >= (ulong)length)
            {
                throw new Mp4FormatException("tfra offset outside input.");
            }
            result?.Add((track, time, (long)offset));
        }
    }
}
