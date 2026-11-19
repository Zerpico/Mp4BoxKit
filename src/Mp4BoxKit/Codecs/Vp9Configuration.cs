using System.Buffers.Binary;

namespace Mp4BoxKit.Codecs;

/// <summary>Builds the VP codec configuration record (<c>vpcC</c>) payload for VP9.</summary>
public static class Vp9Configuration
{
    /// <summary>
    /// Creates a version-1 <c>vpcC</c> payload without its box header.
    /// </summary>
    public static ReadOnlyMemory<byte> CreateVpcC(
        byte profile,
        byte level,
        byte bitDepth,
        byte chromaSubsampling,
        bool videoFullRange,
        byte colourPrimaries = 2,
        byte transferCharacteristics = 2,
        byte matrixCoefficients = 2,
        ReadOnlyMemory<byte> codecInitializationData = default)
    {
        if (profile > 3)
            throw new ArgumentOutOfRangeException(nameof(profile));
        if (bitDepth is not (8 or 10 or 12))
            throw new ArgumentOutOfRangeException(nameof(bitDepth));
        if (profile < 2 && bitDepth != 8)
            throw new ArgumentException("VP9 profiles 0 and 1 require 8-bit samples.", nameof(bitDepth));
        if (chromaSubsampling > 7)
            throw new ArgumentOutOfRangeException(nameof(chromaSubsampling));
        if (codecInitializationData.Length > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(codecInitializationData));

        var payload = new byte[12 + codecInitializationData.Length];
        payload[0] = 1; // FullBox version
        payload[4] = profile;
        payload[5] = level;
        payload[6] = (byte)((bitDepth << 4) | (chromaSubsampling << 1) | (videoFullRange ? 1 : 0));
        payload[7] = colourPrimaries;
        payload[8] = transferCharacteristics;
        payload[9] = matrixCoefficients;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(10), (ushort)codecInitializationData.Length);
        codecInitializationData.Span.CopyTo(payload.AsSpan(12));
        return payload;
    }
}
