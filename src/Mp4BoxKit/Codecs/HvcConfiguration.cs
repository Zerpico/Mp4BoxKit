using System.Buffers.Binary;

namespace Mp4BoxKit.Codecs;

public static class HvcConfiguration
{
    private enum NalType : byte { Vps = 32, Sps = 33, Pps = 34 }

    /// <summary>Creates an hvcC payload from HEVC parameter NAL units without start codes.</summary>
    public static ReadOnlyMemory<byte> CreateFromParameterSets(
        ReadOnlySpan<byte> vps, ReadOnlySpan<byte> sps, ReadOnlySpan<byte> pps)
    {
        ValidateParameterSet(vps, NalType.Vps);
        ValidateParameterSet(sps, NalType.Sps);
        ValidateParameterSet(pps, NalType.Pps);
        var rbsp = new List<byte>();
        var zeros = 0;
        foreach (var b in sps[2..])
        {
            if (zeros >= 2 && b == 3)
            {
                zeros = 0;
                continue;
            }

            rbsp.Add(b);
            zeros = b == 0 ? zeros + 1 : 0;
        }

        if (rbsp.Count < 13) throw new ArgumentException("Truncated HEVC SPS.", nameof(sps));
        var result = new byte[23 + vps.Length + sps.Length + pps.Length + 15];
        result[0] = 1;
        rbsp.CopyTo(1, result, 1, 12); // general profile_tier_level
        result[13] = 0xf0;
        result[15] = 0xfc;
        var subLayers = (rbsp[0] >> 1) & 7;

        if (subLayers > 6)
            throw new ArgumentException("Invalid HEVC sub-layer count.", nameof(sps));

        var bit = 13 * 8 + subLayers * 2 + (subLayers > 0 ? (8 - subLayers) * 2 : 0);
        for (var i = 0; i < subLayers; i++)
        {
            if (ReadBits(rbsp, 13 * 8 + i * 2, 1) != 0) bit += 88;
            if (ReadBits(rbsp, 13 * 8 + i * 2 + 1, 1) != 0) bit += 8;
        }

        ReadUe(rbsp, ref bit); // SPS id
        var chroma = ReadUe(rbsp, ref bit);
        if (chroma > 3)
            throw new ArgumentException("Invalid HEVC chroma format.", nameof(sps));

        if (chroma == 3) bit++;
        ReadUe(rbsp, ref bit);
        ReadUe(rbsp, ref bit); // dimensions

        if (ReadBits(rbsp, bit++, 1) != 0)
        {
            for (var i = 0; i < 4; i++)
                ReadUe(rbsp, ref bit);
        }

        var lumaDepth = ReadUe(rbsp, ref bit);
        var chromaDepth = ReadUe(rbsp, ref bit);

        if (lumaDepth > 7 || chromaDepth > 7)
            throw new ArgumentException("Invalid HEVC bit depth.", nameof(sps));

        result[16] = (byte)(0xfc | chroma);
        result[17] = (byte)(0xf8 | lumaDepth);
        result[18] = (byte)(0xf8 | chromaDepth);
        result[21] = (byte)(((subLayers + 1) << 3) | ((rbsp[0] & 1) << 2) | 3);
        result[22] = 3;
        var offset = 23;

        WriteParameterSet(result, ref offset, NalType.Vps, vps);
        WriteParameterSet(result, ref offset, NalType.Sps, sps);
        WriteParameterSet(result, ref offset, NalType.Pps, pps);
        return result;
    }

    private static void ValidateParameterSet(ReadOnlySpan<byte> nal, NalType type)
    {
        if (nal.Length < 2 || nal.Length > ushort.MaxValue || ((nal[0] >> 1) & 63) != (int)type)
            throw new ArgumentException($"Expected a {type} NAL unit without a start code.");
    }

    private static void WriteParameterSet(Span<byte> output, ref int offset, NalType type, ReadOnlySpan<byte> nal)
    {
        output[offset++] = (byte)(0x80 | (byte)type);
        BinaryPrimitives.WriteUInt16BigEndian(output[offset..], 1);
        offset += 2;
        BinaryPrimitives.WriteUInt16BigEndian(output[offset..], (ushort)nal.Length);
        offset += 2;

        nal.CopyTo(output[offset..]);
        offset += nal.Length;
    }

    private static int ReadBits(List<byte> data, int bit, int count)
    {
        if (bit + count > data.Count * 8)
            throw new ArgumentException("Truncated HEVC SPS.");
        var value = 0;

        for (var i = 0; i < count; i++)
            value = (value << 1) | ((data[(bit + i) / 8] >> (7 - (bit + i) % 8)) & 1);

        return value;
    }

    private static int ReadUe(List<byte> data, ref int bit)
    {
        var zeros = 0;
        while (ReadBits(data, bit++, 1) == 0)
            if (++zeros > 30) throw new ArgumentException("Invalid HEVC SPS.");
        var value = (1 << zeros) - 1 + ReadBits(data, bit, zeros);
        bit += zeros;
        return value;
    }

    internal static int GetNalLengthSize(ReadOnlySpan<byte> hvcC)
        => (hvcC[21] & 3) + 1;

    internal static void Validate(ReadOnlySpan<byte> hvcC)
    {
        if (hvcC.Length < 23)
            throw new ArgumentException("hvcC payload is too short.");
        if (hvcC[0] != 1)
            throw new ArgumentException("hvcC configurationVersion must be 1.");

        var lengthSize = (hvcC[21] & 3) + 1;
        if (lengthSize == 3)
            throw new ArgumentException(
                "hvcC lengthSizeMinusOne=2 (3-byte NAL lengths) is not allowed by ISO-BMFF.");

        var arrayCount = hvcC[22];
        if (arrayCount == 0)
            throw new ArgumentException("hvcC must contain at least one parameter set array.");

        var offset = 23;
        for (var array = 0; array < arrayCount; array++)
        {
            if (offset > hvcC.Length - 3)
                throw new ArgumentException("Truncated hvcC NAL unit array.");

            var count = BinaryPrimitives.ReadUInt16BigEndian(hvcC[(offset + 1)..]);
            offset += 3;
            if (count == 0)
                throw new ArgumentException("hvcC parameter set array is empty.");

            for (var i = 0; i < count; i++)
            {
                if (offset > hvcC.Length - 2)
                    throw new ArgumentException("Truncated hvcC NAL unit length.");

                var length = BinaryPrimitives.ReadUInt16BigEndian(hvcC[offset..]);
                offset += 2;
                if (length == 0 || length > hvcC.Length - offset)
                    throw new ArgumentException("Invalid hvcC NAL unit length.");
                offset += length;
            }
        }
    }
}
