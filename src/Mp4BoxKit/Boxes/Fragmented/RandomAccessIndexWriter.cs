namespace Mp4BoxKit.Boxes.Fragmented;

internal sealed class RandomAccessIndexWriter
{
    private readonly Dictionary<uint, List<(ulong Time, ulong Offset, uint Traf, uint Sample)>> _tracks = new();

    public void Add(uint track, long time, long offset, uint traf, uint sample)
    {
        if (time < 0)
        {
            return;
        }

        if (!_tracks.TryGetValue(track, out var entries))
        {
            entries = new();
            _tracks.Add(track, entries);
        }
        entries.Add(((ulong)time, checked((ulong)offset), traf, sample));
    }

    public byte[] Serialize()
    {
        var buffer = new IsoBoxBuffer();
        var mfra = buffer.BeginBox(BoxTypes.Mfra);
        foreach (var (track, entries) in _tracks)
        {
            var tfra = buffer.BeginBox(BoxTypes.Tfra);
            buffer.WriteUInt32(0x01000000); // version 1
            buffer.WriteUInt32(track);
            buffer.WriteUInt32(0x3f); // all three number fields are 4 bytes
            buffer.WriteUInt32(checked((uint)entries.Count));
            foreach (var entry in entries)
            {
                buffer.WriteUInt64(entry.Time);
                buffer.WriteUInt64(entry.Offset);
                buffer.WriteUInt32(entry.Traf);
                buffer.WriteUInt32(1); // one trun per traf
                buffer.WriteUInt32(entry.Sample);
            }
            buffer.EndBox(tfra);
        }
        var mfro = buffer.BeginBox(BoxTypes.Mfro);
        buffer.WriteUInt32(0);
        buffer.WriteUInt32(0);
        buffer.EndBox(mfro);
        buffer.EndBox(mfra);
        var data = buffer.ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(data.Length - 4),
            checked((uint)data.Length));
        return data;
    }
}
