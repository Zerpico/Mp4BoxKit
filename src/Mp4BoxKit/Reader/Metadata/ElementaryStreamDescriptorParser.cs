namespace Mp4BoxKit.Reader.Metadata;

internal static class ElementaryStreamDescriptorParser
{
    public static Codec ResolveVideoCodec(ReadOnlySpan<byte> esds)
        => esds.Length >= 4 && TryFindObjectType(esds[4..], out var type) && type is >= 0x60 and <= 0x65
            ? Codec.Mpeg2Video : Codec.Mpeg4Video;
    public static Codec ResolveAudioCodec(ReadOnlySpan<byte> esds)
    {
        // esds starts with the FullBox version and flags.
        if (esds.Length >= 4 && TryFindObjectType(esds[4..], out var objectType))
            return objectType is 0x69 or 0x6B ? Codec.Mp3 : Codec.Aac;
        return Codec.Aac;
    }

    private static bool TryFindObjectType(ReadOnlySpan<byte> descriptors, out byte objectType)
    {
        objectType = 0;
        while (!descriptors.IsEmpty)
        {
            var tag = descriptors[0];
            var offset = 1;
            var length = 0;
            var terminated = false;
            for (var i = 0; i < 4 && offset < descriptors.Length; i++)
            {
                var value = descriptors[offset++];
                length = (length << 7) | (value & 0x7F);
                if ((value & 0x80) == 0)
                {
                    terminated = true;
                    break;
                }
            }
            if (!terminated || length > descriptors.Length - offset)
                return false;
            var payload = descriptors.Slice(offset, length);
            if (tag == 0x04 && payload.Length >= 13)
            {
                objectType = payload[0];
                return true;
            }
            if (tag == 0x03)
            {
                if (payload.Length < 3)
                    return false;
                var flags = payload[2];
                var childOffset = 3;
                if ((flags & 0x80) != 0)
                    childOffset += 2;
                if ((flags & 0x40) != 0)
                {
                    if (childOffset >= payload.Length)
                        return false;
                    childOffset += 1 + payload[childOffset];
                }
                if ((flags & 0x20) != 0)
                    childOffset += 2;
                if (childOffset > payload.Length)
                    return false;
                // Only descend into the defined ES wrapper, never arbitrary bytes.
                if (TryFindDecoderConfig(payload[childOffset..], out objectType))
                    return true;
            }
            descriptors = descriptors[(offset + length)..];
        }
        return false;
    }

    private static bool TryFindDecoderConfig(ReadOnlySpan<byte> descriptors, out byte objectType)
    {
        // Nested ES descriptors are not valid children; prevent unbounded recursion.
        objectType = 0;
        while (descriptors.Length >= 2)
        {
            var tag = descriptors[0];
            var offset = 1;
            var length = 0;
            var terminated = false;
            for (var i = 0; i < 4 && offset < descriptors.Length; i++)
            {
                var value = descriptors[offset++];
                length = (length << 7) | (value & 0x7F);
                if ((value & 0x80) == 0)
                {
                    terminated = true;
                    break;
                }
            }
            if (!terminated || length > descriptors.Length - offset)
                return false;
            if (tag == 0x04 && length >= 13)
            {
                objectType = descriptors[offset];
                return true;
            }
            descriptors = descriptors[(offset + length)..];
        }
        return false;
    }
}
