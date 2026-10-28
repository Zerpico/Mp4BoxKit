using System.Buffers.Binary;
using Mp4BoxKit.Writer;

namespace Mp4BoxKit.Boxes.Fragmented;

internal static class SegmentIndexWriter
{
    public static byte[] Serialize(MediaTrackInfo track, IReadOnlyList<PendingSample> samples, int size)
    {
        if ((uint)size > 0x7fffffff)
        {
            throw new NotSupportedException("sidx reference exceeds 31 bits.");
        }
        var first = samples.Min(s => s.Pts);
        var end = samples.Max(s => checked(s.Pts + s.Duration));
        if (first < 0)
        {
            throw new NotSupportedException(
                "sidx requires nonnegative presentation times.");
        }
        var data = new byte[52];
        void U32(int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), value);
        U32(0, 52);
        U32(4, BoxTypes.Sidx.Value);
        U32(8, 0x01000000); // version 1
        U32(12, track.Id.Value);
        U32(16, track.TimeScale);
        BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(20), (ulong)first);
        // first_offset = 0: the referenced moof immediately follows sidx.
        data[39] = 1;
        U32(40, (uint)size);
        U32(44, checked((uint)(end - first)));
        var startsWithSap = track is not VideoTrackInfo ||
            (samples[0].Flags & SampleFlags.KeyFrame) != 0;
        // Claim SAP type 1 only when no presentation reordering precedes the first sample.
        U32(48, startsWithSap && samples[0].Pts == first ? 0x90000000u : 0);
        return data;
    }
}
