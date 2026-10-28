using System.Buffers.Binary;

namespace Mp4BoxKit.Codecs;

/// <summary>
/// Minimal H.264 AVC decoder configuration (avcC) builder and validator.
/// This is not a full SPS parser: it only uses the profile/compatibility/level
/// bytes that are already present in the SPS NAL unit.
/// For High profiles the builder assumes 8-bit 4:2:0 and writes the required
/// avcC extension; supply an already-built avcC for other chroma formats or
/// bit depths.
/// </summary>
public static class AvcConfiguration
{
    /// <summary>
    /// Builds an avcC payload from one SPS NAL unit and one PPS NAL unit.
    /// The NAL units must not contain Annex B start codes.
    /// </summary>
    public static ReadOnlyMemory<byte> CreateFromSpsPps(
        ReadOnlySpan<byte> sps,
        ReadOnlySpan<byte> pps)
    {
        ValidateParameterSet(sps, expectedType: 7, nameof(sps));
        ValidateParameterSet(pps, expectedType: 8, nameof(pps));
        if (sps.Length < 4)
            throw new ArgumentException(
                "SPS is too short to contain profile, compatibility and level bytes.", nameof(sps));

        var highProfile = IsHighProfile(sps[1]);
        var result = new byte[checked(6 + 2 + sps.Length + 1 + 2 + pps.Length +
            (highProfile ? 4 : 0))];
        result[0] = 1; // configurationVersion
        result[1] = sps[1]; // AVCProfileIndication
        result[2] = sps[2]; // profile_compatibility
        result[3] = sps[3]; // AVCLevelIndication
        result[4] = 0xFF; // 4-byte NAL lengths (lengthSizeMinusOne = 3)
        result[5] = 0xE1; // one SPS

        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(6), checked((ushort)sps.Length));
        sps.CopyTo(result.AsSpan(8));

        var offset = 8 + sps.Length;
        result[offset] = 1; // one PPS
        BinaryPrimitives.WriteUInt16BigEndian(
            result.AsSpan(offset + 1), checked((ushort)pps.Length));
        pps.CopyTo(result.AsSpan(offset + 3));

        if (highProfile)
            WriteHighProfileExtension(result.AsSpan(offset + 3 + pps.Length));
        return result;
    }

    /// <summary>
    /// Builds an avcC payload from multiple SPS and PPS NAL units.
    /// </summary>
    public static ReadOnlyMemory<byte> CreateFromSpsPps(
        IReadOnlyList<ReadOnlyMemory<byte>> sps,
        IReadOnlyList<ReadOnlyMemory<byte>> pps)
    {
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);
        if (sps.Count is 0 or > 31)
            throw new ArgumentOutOfRangeException(nameof(sps), "avcC supports between 1 and 31 SPS entries.");
        if (pps.Count is 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(pps), "avcC supports between 1 and 255 PPS entries.");
        if (sps[0].Length < 4)
            throw new ArgumentException(
                "SPS is too short to contain profile, compatibility and level bytes.", nameof(sps));

        var highProfile = IsHighProfile(sps[0].Span[1]);
        var size = 6;
        foreach (var item in sps)
        {
            ValidateParameterSet(item.Span, expectedType: 7, nameof(sps));
            size = checked(size + 2 + item.Length);
        }

        size = checked(size + 1);
        foreach (var item in pps)
        {
            ValidateParameterSet(item.Span, expectedType: 8, nameof(pps));
            size = checked(size + 2 + item.Length);
        }

        if (highProfile)
            size = checked(size + 4);

        var result = new byte[size];
        result[0] = 1;
        result[1] = sps[0].Span[1];
        result[2] = sps[0].Span[2];
        result[3] = sps[0].Span[3];
        result[4] = 0xFF;
        result[5] = (byte)(0xE0 | sps.Count);

        var offset = 6;
        foreach (var item in sps)
        {
            BinaryPrimitives.WriteUInt16BigEndian(
                result.AsSpan(offset), checked((ushort)item.Length));
            offset += 2;
            item.Span.CopyTo(result.AsSpan(offset));
            offset += item.Length;
        }

        result[offset++] = (byte)pps.Count;
        foreach (var item in pps)
        {
            BinaryPrimitives.WriteUInt16BigEndian(
                result.AsSpan(offset), checked((ushort)item.Length));
            offset += 2;
            item.Span.CopyTo(result.AsSpan(offset));
            offset += item.Length;
        }

        if (highProfile)
            WriteHighProfileExtension(result.AsSpan(offset));
        return result;
    }

    internal static int GetNalLengthSize(ReadOnlySpan<byte> avcC)
        => (avcC[4] & 3) + 1;

    internal static void Validate(ReadOnlySpan<byte> avcC)
    {
        if (avcC.Length < 7)
            throw new ArgumentException("avcC payload is too short.");
        if (avcC[0] != 1)
            throw new ArgumentException("avcC configurationVersion must be 1.");

        var lengthSize = (avcC[4] & 3) + 1;
        if (lengthSize == 3)
            throw new ArgumentException(
                "avcC lengthSizeMinusOne=2 (3-byte NAL lengths) is not allowed by ISO-BMFF.");

        var spsCount = avcC[5] & 0x1F;
        if (spsCount == 0)
            throw new ArgumentException("avcC must contain at least one SPS.");

        var offset = 6;
        for (var i = 0; i < spsCount; i++)
            offset = SkipNal(avcC, offset, "SPS");

        if (offset >= avcC.Length)
            throw new ArgumentException("avcC is missing the PPS count.");

        var ppsCount = avcC[offset++];
        if (ppsCount == 0)
            throw new ArgumentException("avcC must contain at least one PPS.");

        for (var i = 0; i < ppsCount; i++)
            offset = SkipNal(avcC, offset, "PPS");
    }

    private static bool IsHighProfile(byte profileIdc)
        => profileIdc is 100 or 110 or 122 or 144 or 244;

    private static void WriteHighProfileExtension(Span<byte> destination)
    {
        // The minimal builder assumes 8-bit 4:2:0 for High profile streams.
        // Pass an already-built avcC when chroma format or bit depth differs.
        destination[0] = 0xFD; // reserved (6) | chroma_format = 1 (4:2:0)
        destination[1] = 0xF8; // reserved (5) | bit_depth_luma_minus8 = 0
        destination[2] = 0xF8; // reserved (5) | bit_depth_chroma_minus8 = 0
        destination[3] = 0;    // numOfSequenceParameterSetExt
    }

    private static int SkipNal(ReadOnlySpan<byte> data, int offset, string name)
    {
        if (offset > data.Length - 2)
            throw new ArgumentException($"Truncated avcC {name} length.");
        var length = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
        offset += 2;
        if (length == 0 || length > data.Length - offset)
            throw new ArgumentException($"Invalid avcC {name} length.");
        return offset + length;
    }

    private static void ValidateParameterSet(
        ReadOnlySpan<byte> nal,
        int expectedType,
        string parameterName)
    {
        if (nal.IsEmpty)
            throw new ArgumentException("Parameter set NAL unit is empty.", parameterName);
        var type = nal[0] & 0x1F;
        if (type != expectedType)
            throw new ArgumentException(
                $"Expected H.264 NAL type {expectedType}, got {type}. Pass a raw SPS/PPS NAL unit without start codes.",
                parameterName);
        if (nal.Length > ushort.MaxValue)
            throw new ArgumentException("Parameter set NAL unit is too large for avcC.", parameterName);
    }
}
