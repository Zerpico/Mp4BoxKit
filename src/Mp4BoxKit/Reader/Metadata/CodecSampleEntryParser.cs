using Mp4BoxKit.Boxes;
using Mp4BoxKit.Codecs;

namespace Mp4BoxKit.Reader.Metadata;

/// <summary>
/// Parses codec-specific fields from an ISO-BMFF sample entry.
/// Container traversal stays in Mp4Reader; codec layout stays here.
/// </summary>
internal static class CodecSampleEntryParser
{
    public static void Parse(ReadOnlySpan<byte> data, TrackMetadata metadata)
    {
        if (data.Length < 8)
        {
            return;
        }

        var entrySize = BigEndian.U32(data);
        var type = new FourCc(BigEndian.U32(data[4..]));
        metadata.Codec = CodecRegistry.FromSampleEntry(type);

        var entryLength = Math.Min(
            data.Length,
            checked((int)Math.Max(8, entrySize)));

        if (CodecRegistry.IsVisual(metadata.Codec))
        {
            ParseVisualFields(data, entryLength, metadata);
        }
        else if (metadata.MediaType == MediaType.Audio &&
                 entryLength >= 36)
        {
            metadata.Channels = BigEndian.U16(data[24..]);
            metadata.BitsPerSample = BigEndian.U16(data[26..]);
            metadata.PcmLittleEndian16 = type == new FourCc("sowt") && metadata.BitsPerSample == 16;
            metadata.SampleRate = checked(
                (int)(BigEndian.U32(data[32..]) >> 16));
        }

        ParseChildBoxes(data, entryLength, metadata);
    }

    private static void ParseVisualFields(
        ReadOnlySpan<byte> data,
        int entryLength,
        TrackMetadata metadata)
    {
        if (entryLength < 36)
        {
            return;
        }

        metadata.Width = BigEndian.U16(data[32..]);
        metadata.Height = BigEndian.U16(data[34..]);
    }

    private static void ParseChildBoxes(
        ReadOnlySpan<byte> data,
        int entryLength,
        TrackMetadata metadata)
    {
        var childOffset = CodecRegistry.IsVisual(metadata.Codec) ? 86 : 16;
        if (metadata.MediaType == MediaType.Audio)
        {
            if (entryLength < 36)
                return;
            var version = BigEndian.U16(data[16..]);
            childOffset = version switch { 0 => 36, 1 => 52, 2 => 72, _ => entryLength };
        }

        while (childOffset + 8 <= entryLength)
        {
            var childSize = BigEndian.U32(data[childOffset..]);
            var childType = new FourCc(
                BigEndian.U32(data[(childOffset + 4)..]));
            if (childSize < 8 || childSize > entryLength - childOffset)
            {
                break;
            }

            if (CodecConfigurationBoxes.IsConfiguration(childType))
            {
                var configuration = data
                    .Slice(childOffset + 8, checked((int)childSize - 8))
                    .ToArray();
                metadata.CodecConfiguration = configuration;
                if (metadata.Codec == Codec.Mpeg4Video && childType == BoxTypes.Esds)
                    metadata.Codec = ElementaryStreamDescriptorParser.ResolveVideoCodec(configuration);
                if (metadata.Codec == Codec.Aac &&
                    childType == BoxTypes.Esds)
                {
                    metadata.Codec = ElementaryStreamDescriptorParser.ResolveAudioCodec(
                        configuration);
                }
            }

            childOffset += checked((int)childSize);
        }

    }

}
