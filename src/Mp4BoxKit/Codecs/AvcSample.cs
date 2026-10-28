using System.Buffers.Binary;

namespace Mp4BoxKit.Codecs;

/// <summary>
/// Converts H.264 access units to the length-prefixed AVCC sample format
/// expected by <see cref="Mp4Writer.WriteSample"/>.
/// </summary>
public static class AvcSample
{
    /// <summary>
    /// Converts an Annex B access unit (with 3- or 4-byte start codes) to AVCC.
    /// Parameter sets are omitted by default because avc1 keeps them in avcC.
    /// </summary>
    public static ReadOnlyMemory<byte> FromAnnexB(
        ReadOnlySpan<byte> data,
        bool includeParameterSets = false)
    {
        var ranges = new List<(int Start, int Length)>();
        var start = -1;
        for (var i = 0; i + 2 < data.Length; i++)
        {
            if (data[i] != 0 || data[i + 1] != 0)
                continue;

            var prefix = data[i + 2] == 1
                ? 3
                : i + 3 < data.Length && data[i + 2] == 0 && data[i + 3] == 1 ? 4 : 0;
            if (prefix == 0)
                continue;

            if (start >= 0)
                AddRange(data, ranges, start, i - start, includeParameterSets);
            start = i + prefix;
            i = start - 1;
        }

        if (start < 0)
            throw new ArgumentException("Input does not contain Annex B start codes; use FromNalUnits instead.", nameof(data));
        AddRange(data, ranges, start, data.Length - start, includeParameterSets);
        return WriteRanges(data, ranges, nameof(data));
    }

    private static void AddRange(
        ReadOnlySpan<byte> data,
        List<(int Start, int Length)> ranges,
        int start,
        int length,
        bool includeParameterSets)
    {
        if (length <= 0)
            return;
        var type = data[start] & 0x1F;
        if (!includeParameterSets && type is 7 or 8)
            return;
        ranges.Add((start, length));
    }

    /// <summary>
    /// Joins separate NAL units into a length-prefixed AVCC sample.
    /// Parameter sets are omitted by default because avc1 keeps them in avcC.
    /// </summary>
    public static ReadOnlyMemory<byte> FromNalUnits(
        IEnumerable<ReadOnlyMemory<byte>> nalUnits,
        bool includeParameterSets = false)
    {
        ArgumentNullException.ThrowIfNull(nalUnits);
        var selected = new List<ReadOnlyMemory<byte>>();
        foreach (var nal in nalUnits)
        {
            if (nal.IsEmpty)
                continue;
            var type = nal.Span[0] & 0x1F;
            if (!includeParameterSets && type is 7 or 8)
                continue;
            selected.Add(nal);
        }

        if (selected.Count == 0)
            throw new ArgumentException(
                "No NAL units remain after filtering parameter sets.", nameof(nalUnits));

        var size = 0;
        foreach (var nal in selected)
            size = checked(size + 4 + nal.Length);

        var result = new byte[size];
        var offset = 0;
        foreach (var nal in selected)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                result.AsSpan(offset), checked((uint)nal.Length));
            offset += 4;
            nal.Span.CopyTo(result.AsSpan(offset));
            offset += nal.Length;
        }

        return result;
    }

    private static ReadOnlyMemory<byte> WriteRanges(
        ReadOnlySpan<byte> data,
        List<(int Start, int Length)> ranges,
        string parameterName)
    {
        if (ranges.Count == 0)
            throw new ArgumentException(
                "No NAL units remain after filtering parameter sets.", parameterName);

        var size = 0;
        foreach (var range in ranges)
            size = checked(size + 4 + range.Length);

        var result = new byte[size];
        var offset = 0;
        foreach (var range in ranges)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                result.AsSpan(offset), checked((uint)range.Length));
            offset += 4;
            data.Slice(range.Start, range.Length).CopyTo(result.AsSpan(offset));
            offset += range.Length;
        }

        return result;
    }
}
