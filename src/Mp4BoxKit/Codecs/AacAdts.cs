namespace Mp4BoxKit.Codecs;

public readonly record struct AacAdtsHeader(
    int AudioObjectType,
    int FrequencyIndex,
    int SampleRate,
    int ChannelConfiguration,
    int HeaderLength,
    int FrameLength);

/// <summary>
/// Minimal ADTS parser for AAC-LC streams. It extracts the raw AAC access
/// unit and the parameters needed for an AudioSpecificConfig/esds.
/// Multiple raw data blocks per ADTS frame are not supported.
/// </summary>
public static class AacAdts
{
    public static bool TryParse(ReadOnlySpan<byte> frame, out AacAdtsHeader header)
    {
        header = default;
        if (frame.Length < 7)
            return false;
        if (frame[0] != 0xFF || (frame[1] & 0xF0) != 0xF0)
            return false;

        var protectionAbsent = (frame[1] & 1) != 0;
        var headerLength = protectionAbsent ? 7 : 9;
        if (frame.Length < headerLength)
            return false;

        var audioObjectType = ((frame[2] >> 6) & 3) + 1;
        var frequencyIndex = (frame[2] >> 2) & 0xF;
        if (frequencyIndex == 0xF)
            return false; // explicit frequency is not represented in ADTS

        var channelConfiguration =
            ((frame[2] & 1) << 2) | ((frame[3] >> 6) & 3);
        if (channelConfiguration == 0)
            return false; // Program Config Element is not supported

        var frameLength =
            ((frame[3] & 3) << 11) |
            (frame[4] << 3) |
            ((frame[5] >> 5) & 7);
        if (frameLength < headerLength || frameLength > frame.Length)
            return false;

        // number_of_raw_data_blocks_in_frame
        if ((frame[6] & 3) != 0)
            return false;

        header = new AacAdtsHeader(
            audioObjectType,
            frequencyIndex,
            AacConfiguration.GetSampleRate(frequencyIndex),
            channelConfiguration,
            headerLength,
            frameLength);
        return true;
    }

    public static ReadOnlySpan<byte> GetPayload(ReadOnlySpan<byte> frame)
    {
        if (!TryParse(frame, out var header))
            throw new ArgumentException("Invalid or unsupported ADTS frame.", nameof(frame));
        return frame[header.HeaderLength..header.FrameLength];
    }

    /// <summary>Builds an esds for the AAC parameters carried by an ADTS frame.</summary>
    public static ReadOnlyMemory<byte> CreateEsds(ReadOnlySpan<byte> frame)
    {
        if (!TryParse(frame, out var header))
            throw new ArgumentException("Invalid or unsupported ADTS frame.", nameof(frame));
        var asc = AacConfiguration.CreateAudioSpecificConfigFromIndices(
            header.AudioObjectType, header.FrequencyIndex, header.ChannelConfiguration);
        return AacConfiguration.CreateEsdsFromAudioSpecificConfig(asc.Span);
    }
}
