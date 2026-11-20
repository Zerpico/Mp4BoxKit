using System.Buffers.Binary;

namespace Mp4BoxKit.Codecs;

/// <summary>Builds the OpusSpecificBox payload (<c>dOps</c>) used by MP4 audio sample entries.</summary>
public static class OpusConfiguration
{
    /// <summary>
    /// Creates the <c>dOps</c> payload without its box header. For mapping family zero,
    /// only mono and stereo are valid and the mapping fields must be omitted.
    /// </summary>
    public static ReadOnlyMemory<byte> CreateDops(
        byte outputChannelCount,
        ushort preSkip = 0,
        uint inputSampleRate = 48_000,
        short outputGain = 0,
        byte channelMappingFamily = 0,
        byte streamCount = 0,
        byte coupledCount = 0,
        ReadOnlyMemory<byte> channelMapping = default)
    {
        if (outputChannelCount == 0)
            throw new ArgumentOutOfRangeException(nameof(outputChannelCount));

        if (channelMappingFamily == 0)
        {
            if (outputChannelCount > 2)
                throw new ArgumentOutOfRangeException(nameof(outputChannelCount),
                    "Opus mapping family zero supports mono or stereo only.");
            if (streamCount != 0 || coupledCount != 0 || !channelMapping.IsEmpty)
                throw new ArgumentException("Mapping-family fields must be omitted for family zero.", nameof(channelMapping));
        }
        else
        {
            if (streamCount == 0)
                throw new ArgumentOutOfRangeException(nameof(streamCount));
            if (coupledCount > streamCount)
                throw new ArgumentOutOfRangeException(nameof(coupledCount));
            if (channelMapping.Length != outputChannelCount)
                throw new ArgumentException("A channel mapping entry is required for each output channel.", nameof(channelMapping));

            var codedChannels = streamCount + coupledCount;
            foreach (var mapping in channelMapping.Span)
                if (mapping != byte.MaxValue && mapping >= codedChannels)
                    throw new ArgumentOutOfRangeException(nameof(channelMapping),
                        "Channel mapping entries must refer to a coded channel or 255 (silence).");
        }

        var payload = new byte[11 + (channelMappingFamily == 0 ? 0 : 2 + channelMapping.Length)];
        payload[0] = 0; // dOps version
        payload[1] = outputChannelCount;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2), preSkip);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4), inputSampleRate);
        BinaryPrimitives.WriteInt16BigEndian(payload.AsSpan(8), outputGain);
        payload[10] = channelMappingFamily;

        if (channelMappingFamily != 0)
        {
            payload[11] = streamCount;
            payload[12] = coupledCount;
            channelMapping.Span.CopyTo(payload.AsSpan(13));
        }

        return payload;
    }
}
