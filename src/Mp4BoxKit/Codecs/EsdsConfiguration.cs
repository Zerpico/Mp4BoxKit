using System.Buffers.Binary;

namespace Mp4BoxKit.Codecs;

/// <summary>Builds MPEG-4 Systems <c>esds</c> payloads for codecs using ES descriptors.</summary>
public static class EsdsConfiguration
{
    /// <summary>
    /// Creates an <c>esds</c> payload without its box header. <paramref name="streamType"/>
    /// is the six-bit MPEG-4 Systems stream type (4 for visual, 5 for audio).
    /// </summary>
    public static ReadOnlyMemory<byte> Create(
        byte objectTypeIndication,
        byte streamType,
        ReadOnlyMemory<byte> decoderSpecificInfo = default,
        ushort esId = 1,
        uint bufferSizeDb = 0,
        uint maxBitrate = 0,
        uint averageBitrate = 0)
    {
        if (streamType > 0x3f)
            throw new ArgumentOutOfRangeException(nameof(streamType));
        if (bufferSizeDb > 0x00ff_ffff)
            throw new ArgumentOutOfRangeException(nameof(bufferSizeDb));

        using var decoderConfig = new MemoryStream();
        decoderConfig.WriteByte(objectTypeIndication);
        decoderConfig.WriteByte((byte)((streamType << 2) | 1));
        decoderConfig.WriteByte((byte)(bufferSizeDb >> 16));
        decoderConfig.WriteByte((byte)(bufferSizeDb >> 8));
        decoderConfig.WriteByte((byte)bufferSizeDb);
        WriteUInt32(decoderConfig, maxBitrate);
        WriteUInt32(decoderConfig, averageBitrate);
        if (!decoderSpecificInfo.IsEmpty)
            WriteDescriptor(decoderConfig, 0x05, decoderSpecificInfo.Span);

        using var esDescriptor = new MemoryStream();
        WriteUInt16(esDescriptor, esId);
        esDescriptor.WriteByte(0); // flags
        WriteDescriptor(esDescriptor, 0x04, decoderConfig.ToArray());
        WriteDescriptor(esDescriptor, 0x06, new byte[] { 2 }); // SLConfigDescriptor

        using var result = new MemoryStream();
        result.Write(new byte[4]); // FullBox version and flags
        WriteDescriptor(result, 0x03, esDescriptor.ToArray());
        return result.ToArray();
    }

    private static void WriteDescriptor(Stream output, byte tag, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > 0x0fff_ffff)
            throw new ArgumentOutOfRangeException(nameof(payload));

        output.WriteByte(tag);
        Span<byte> encodedLength = stackalloc byte[4];
        var lengthBytes = EncodeLength(encodedLength, payload.Length);
        output.Write(encodedLength[..lengthBytes]);
        output.Write(payload);
    }

    private static int EncodeLength(Span<byte> output, int length)
    {
        var byteCount = 1;
        for (var remaining = length; remaining >= 0x80; remaining >>= 7)
            byteCount++;

        for (var i = 0; i < byteCount; i++)
        {
            var shift = (byteCount - i - 1) * 7;
            output[i] = (byte)((length >> shift) & 0x7f);
            if (i < byteCount - 1)
                output[i] |= 0x80;
        }

        return byteCount;
    }

    private static void WriteUInt16(Stream output, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        output.Write(bytes);
    }

    private static void WriteUInt32(Stream output, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        output.Write(bytes);
    }
}
