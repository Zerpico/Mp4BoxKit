using System.Buffers;
using Mp4BoxKit.Writer;
using System.Buffers.Binary;
using Mp4BoxKit.Boxes;
using Mp4BoxKit.Boxes.Fragmented;

namespace Mp4BoxKit.Writer.Fragments;

internal static class MediaFragmentWriter
{
    internal static byte[] Serialize(
        IReadOnlyList<MediaTrackInfo> tracks,
        IReadOnlyList<List<PendingSample>> pending,
        uint sequence)
    {
        var moof = SerializeMoof(tracks, pending, sequence, out var payloadSize);
        var result = new byte[checked(moof.Length + 8 + payloadSize)];
        moof.CopyTo(result, 0);
        BigEndian.W32(result.AsSpan(moof.Length), checked((uint)(8 + payloadSize)));
        BigEndian.W32(result.AsSpan(moof.Length + 4), BoxTypes.Mdat.Value);
        var position = moof.Length + 8;
        foreach (var samples in pending)
            foreach (var sample in samples)
            {
                sample.Data.CopyTo(result.AsSpan(position));
                position += sample.Data.Length;
            }
        return result;
    }

    /// <summary>Builds only the moof box; mdat payload is written separately.</summary>
    internal static byte[] SerializeMoof(
        IReadOnlyList<MediaTrackInfo> tracks,
        IReadOnlyList<List<PendingSample>> pending,
        uint sequence,
        out int payloadSize)
    {
        payloadSize = 0;
        foreach (var samples in pending)
            foreach (var sample in samples)
                payloadSize = checked(payloadSize + sample.Data.Length);

        var initialMoof = BuildMoof(
            tracks,
            pending,
            sequence,
            new int[tracks.Count]);
        var offsets = CalculateDataOffsets(tracks, pending, initialMoof.Length);
        return BuildMoof(tracks, pending, sequence, offsets);
    }


    private static int[] CalculateDataOffsets(
        IReadOnlyList<MediaTrackInfo> tracks,
        IReadOnlyList<List<PendingSample>> pending,
        int moofLength)
    {
        var offsets = new int[tracks.Count];
        var payloadOffset = 0;
        var dataOffset = checked(moofLength + 8);

        for (var i = 0; i < tracks.Count; i++)
        {
            if (pending[i].Count == 0)
            {
                continue;
            }

            offsets[i] = checked(dataOffset + payloadOffset);
            payloadOffset += pending[i].Sum(sample => sample.Data.Length);
        }

        return offsets;
    }


    private static byte[] BuildMoof(
        IReadOnlyList<MediaTrackInfo> tracks,
        IReadOnlyList<List<PendingSample>> pending,
        uint sequence,
        int[] offsets)
    {
        return new MovieFragmentBox(
            tracks,
            pending,
            sequence,
            offsets).Serialize();
    }

}

