namespace Mp4BoxKit.Codecs;

/// <summary>
/// Builds a minimal MPEG-4 AudioSpecificConfig and esds for AAC-LC.
/// For HE-AAC/SBR/PS or non-standard channel configurations, pass an
/// already-built esds supplied by the encoder or stream parser.
/// </summary>
public static class AacConfiguration
{
    private static readonly int[] SamplingFrequencies =
    {
        96_000, 88_200, 64_000, 48_000, 44_100, 32_000,
        24_000, 22_050, 16_000, 12_000, 11_025, 8_000, 7_350
    };

    /// <summary>
    /// Builds a two- or five-byte AudioSpecificConfig for AAC-LC
    /// (or another GA object type in the range 1..4).
    /// </summary>
    public static ReadOnlyMemory<byte> CreateAudioSpecificConfig(
        int sampleRate,
        int channels,
        int audioObjectType = 2)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels is < 1 or > 7)
            throw new NotSupportedException(
                "AAC channel configurations above 7 require a Program Config Element.");
        if (audioObjectType is < 1 or > 4)
            throw new NotSupportedException(
                "Only AAC GA object types 1..4 are supported by the minimal builder.");

        var frequencyIndex = Array.IndexOf(SamplingFrequencies, sampleRate);
        return frequencyIndex >= 0
            ? CreateAudioSpecificConfigFromIndices(
                audioObjectType, frequencyIndex, channels)
            : CreateAudioSpecificConfigFromIndices(
                audioObjectType, 0xF, channels, sampleRate);
    }

    internal static ReadOnlyMemory<byte> CreateAudioSpecificConfigFromIndices(
        int audioObjectType,
        int frequencyIndex,
        int channelConfiguration,
        int explicitSampleRate = -1)
    {
        if (audioObjectType is < 1 or > 4)
            throw new NotSupportedException(
                "Only AAC GA object types 1..4 are supported by the minimal builder.");
        if (channelConfiguration is < 1 or > 7)
            throw new NotSupportedException(
                "AAC channel configurations above 7 require a Program Config Element.");

        var bits = new List<bool>();
        Write(bits, audioObjectType, 5);
        Write(bits, frequencyIndex, 4);
        if (frequencyIndex == 0xF)
        {
            if (explicitSampleRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(explicitSampleRate));
            Write(bits, explicitSampleRate, 24);
        }
        else if (explicitSampleRate >= 0)
        {
            throw new ArgumentException(
                "Explicit sample rate is only valid with frequency index 15.");
        }

        Write(bits, channelConfiguration, 4);
        Write(bits, 0, 1); // frameLengthFlag
        Write(bits, 0, 1); // dependsOnCoreCoder
        Write(bits, 0, 1); // extensionFlag

        var result = new byte[(bits.Count + 7) / 8];
        for (var i = 0; i < bits.Count; i++)
            if (bits[i])
                result[i / 8] |= (byte)(1 << (7 - i % 8));
        return result;
    }

    internal static int GetSampleRate(int frequencyIndex)
    {
        if (frequencyIndex < 0 || frequencyIndex >= SamplingFrequencies.Length)
            throw new ArgumentOutOfRangeException(nameof(frequencyIndex));
        return SamplingFrequencies[frequencyIndex];
    }

    /// <summary>
    /// Builds an esds payload (without box header) for AAC-LC.
    /// </summary>
    public static ReadOnlyMemory<byte> CreateEsds(
        int sampleRate,
        int channels,
        int audioObjectType = 2)
        => CreateEsdsFromAudioSpecificConfig(
            CreateAudioSpecificConfig(sampleRate, channels, audioObjectType).Span);

    /// <summary>
    /// Wraps an existing AudioSpecificConfig (for example encoder extradata)
    /// into an esds payload.
    /// </summary>
    public static ReadOnlyMemory<byte> CreateEsdsFromAudioSpecificConfig(
        ReadOnlySpan<byte> audioSpecificConfig)
    {
        if (audioSpecificConfig.IsEmpty)
            throw new ArgumentException(
                "AudioSpecificConfig is empty.", nameof(audioSpecificConfig));

        var decoderSpecific = Descriptor(0x05, audioSpecificConfig.ToArray());
        var decoderConfig = Descriptor(0x04, new byte[]
        {
            0x40, // MPEG-4 Audio
            0x15, // AudioStream
            0, 0, 0, // bufferSizeDB
            0, 0, 0, 0, // maxBitrate
            0, 0, 0, 0  // avgBitrate
        }.Concat(decoderSpecific).ToArray());
        var slConfig = Descriptor(0x06, new byte[] { 2 });
        var esPayload = new byte[] { 0, 1, 0 }
            .Concat(decoderConfig)
            .Concat(slConfig)
            .ToArray();
        return new byte[4].Concat(Descriptor(0x03, esPayload)).ToArray();
    }

    private static byte[] Descriptor(byte tag, byte[] payload)
    {
        using var result = new MemoryStream();
        result.WriteByte(tag);
        result.WriteByte((byte)(0x80 | (payload.Length >> 21 & 127)));
        result.WriteByte((byte)(0x80 | (payload.Length >> 14 & 127)));
        result.WriteByte((byte)(0x80 | (payload.Length >> 7 & 127)));
        result.WriteByte((byte)(payload.Length & 127));
        result.Write(payload);
        return result.ToArray();
    }

    private static void Write(List<bool> bits, int value, int width)
    {
        for (var i = width - 1; i >= 0; i--)
            bits.Add(((value >> i) & 1) != 0);
    }
}
