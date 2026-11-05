using System.Buffers.Binary;

namespace Mp4BoxKit.Codecs;

/// <summary>
/// Converts H.264/H.265 access units to the four-byte length-prefixed sample format
/// expected by <see cref="Mp4Writer.WriteSample"/>.
/// </summary>
public static class NalSampleConverter
{
    private enum AvcNalType : byte { Sps = 7, Pps = 8 }
    private enum HevcNalType : byte { Vps = 32, Sps = 33, Pps = 34 }

    /// <summary>
    /// Converts one Annex B access unit (with 3- or 4-byte start codes).
    /// Parameter sets are omitted by default; store them in avcC/hvcC instead.
    /// </summary>
    public static ReadOnlyMemory<byte> FromAnnexB(ReadOnlySpan<byte> data, Codec codec, bool includeParameterSets = false)
    {
        ValidateCodec(codec);
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
                AddRange(data, ranges, start, i - start, codec, includeParameterSets);
            start = i + prefix;
            i = start - 1;
        }

        if (start < 0)
            throw new ArgumentException("Input does not contain Annex B start codes; use FromNalUnits instead.", nameof(data));
        AddRange(data, ranges, start, data.Length - start, codec, includeParameterSets);
        return WriteRanges(data, ranges, nameof(data));
    }

    private static void AddRange(
        ReadOnlySpan<byte> data,
        List<(int Start, int Length)> ranges,
        int start,
        int length,
        Codec codec,
        bool includeParameterSets)
    {
        if (length <= 0)
            return;
        var parameterSet = IsParameterSet(data.Slice(start, length), codec);
        if (!includeParameterSets && parameterSet)
            return;
        ranges.Add((start, length));
    }

    /// <summary>
    /// Joins raw NAL units of one access unit into a four-byte length-prefixed sample.
    /// Parameter sets are omitted by default; store them in avcC/hvcC instead.
    /// </summary>
    public static ReadOnlyMemory<byte> FromNalUnits(
        IEnumerable<ReadOnlyMemory<byte>> nalUnits,
        Codec codec,
        bool includeParameterSets = false)
    {
        ArgumentNullException.ThrowIfNull(nalUnits);
        ValidateCodec(codec);
        var selected = new List<ReadOnlyMemory<byte>>();
        foreach (var nal in nalUnits)
        {
            if (nal.IsEmpty)
                continue;
            var parameterSet = IsParameterSet(nal.Span, codec);
            if (!includeParameterSets && parameterSet)
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

    private static void ValidateCodec(Codec codec)
    {
        if (codec is not (Codec.H264 or Codec.H265))
            throw new ArgumentOutOfRangeException(nameof(codec), "Only H.264 and H.265 NAL samples are supported.");
    }

    private static bool IsParameterSet(ReadOnlySpan<byte> nal, Codec codec)
    {
        if (codec == Codec.H264)
            return (AvcNalType)(nal[0] & 0x1F) is AvcNalType.Sps or AvcNalType.Pps;
        if (nal.Length < 2)
            throw new ArgumentException("H.265 NAL unit requires a two-byte header.");
        return (HevcNalType)((nal[0] >> 1) & 0x3F) is HevcNalType.Vps or HevcNalType.Sps or HevcNalType.Pps;
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
