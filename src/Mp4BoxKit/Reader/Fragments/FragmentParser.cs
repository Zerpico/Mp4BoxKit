using Mp4BoxKit.Boxes;

namespace Mp4BoxKit.Reader.Fragments;

internal sealed class FragmentParser
{
    private readonly Dictionary<uint, (uint Duration, uint Size, uint Flags)> _defaults = new();

    public void ReadMovieDefaults(ReadOnlySpan<byte> movie)
    {
        _defaults.Clear();
        var boxes = new IsoBoxReader(movie);
        while (boxes.TryRead(out var type, out var payload))
        {
            if (type != BoxTypes.Mvex)
            {
                continue;
            }
            var children = new IsoBoxReader(payload);
            while (children.TryRead(out var child, out var entry))
            {
                if (child != BoxTypes.Trex)
                {
                    continue;
                }

                if (entry.Length < 24)
                {
                    throw new Mp4FormatException("Truncated trex box.");
                }
                _defaults[BigEndian.U32(entry[4..])] =
                    (BigEndian.U32(entry[12..]), BigEndian.U32(entry[16..]), BigEndian.U32(entry[20..]));
            }
        }
    }

    public List<FragmentTrack> Parse(ReadOnlySpan<byte> moof, int? mediaPayloadOffset = null, long moofStart = 0)
        => TrackFragmentParser.Parse(moof, _defaults, mediaPayloadOffset ?? checked(moof.Length + 16), moofStart);

    private static class TrackFragmentParser
    {
        private const uint TfhdBaseDataOffsetPresent = 0x000001;
        private const uint TfhdSampleDescriptionIndexPresent = 0x000002;
        private const uint TfhdDefaultSampleDurationPresent = 0x000008;
        private const uint TfhdDefaultSampleSizePresent = 0x000010;
        private const uint TfhdDefaultSampleFlagsPresent = 0x000020;

        public static List<FragmentTrack> Parse(ReadOnlySpan<byte> moof,
            IReadOnlyDictionary<uint, (uint Duration, uint Size, uint Flags)> defaults,
            int mediaPayloadOffset, long moofStart)
        {
            var result = new List<FragmentTrack>();
            var mediaOffset = 0;
            var boxes = new IsoBoxReader(moof);

            while (boxes.TryRead(out var type, out var payload))
            {
                if (type != BoxTypes.Traf)
                {
                    continue;
                }

                var track = ParseTrackFragment(
                    payload,
                    mediaPayloadOffset,
                    ref mediaOffset, defaults, moofStart, result.Count == 0);
                if (track is not null)
                {
                    result.Add(track);
                }
            }

            return result;
        }

        private static FragmentTrack? ParseTrackFragment(
            ReadOnlySpan<byte> traf,
            int mediaPayloadOffset,
            ref int mediaOffset,
            IReadOnlyDictionary<uint, (uint Duration, uint Size, uint Flags)> defaults,
            long moofStart, bool firstTrack)
        {
            uint trackId = 0;
            uint tfhdFlags = 0;
            uint defaultDuration = 0;
            uint defaultSize = 0;
            uint defaultFlags = 0;
            long decodeTime = 0;
            long baseOffset = firstTrack ? -mediaPayloadOffset : mediaOffset;
            var firstRun = true;
            var hasDecodeTime = false;
            var samples = new List<FragmentSample>();
            var boxes = new IsoBoxReader(traf);

            while (boxes.TryRead(out var type, out var payload))
            {
                if (type == BoxTypes.Tfhd && payload.Length >= 8)
                {
                    tfhdFlags = BigEndian.U32(payload[0..]) & 0x00ffffff;
                    trackId = BigEndian.U32(payload[4..]);
                    if ((tfhdFlags & TfhdBaseDataOffsetPresent) != 0)
                    {
                        if (payload.Length < 16)
                        {
                            throw new Mp4FormatException(
                                "Truncated tfhd base data offset.");
                        }
                        baseOffset = checked((long)BigEndian.U64(payload[8..]) - moofStart - mediaPayloadOffset);
                    }
                    else if ((tfhdFlags & 0x020000) != 0)
                    {
                        baseOffset = -mediaPayloadOffset;
                    }
                    defaults.TryGetValue(trackId, out var inherited);
                    ReadTfhdDefaults(
                        payload,
                        tfhdFlags,
                        out defaultDuration,
                        out defaultSize,
                        out defaultFlags);
                    if ((tfhdFlags & TfhdDefaultSampleDurationPresent) == 0)
                    {
                        defaultDuration = inherited.Duration;
                    }

                    if ((tfhdFlags & TfhdDefaultSampleSizePresent) == 0)
                    {
                        defaultSize = inherited.Size;
                    }

                    if ((tfhdFlags & TfhdDefaultSampleFlagsPresent) == 0)
                    {
                        defaultFlags = inherited.Flags;
                    }
                }
                else if (type == BoxTypes.Tfdt)
                {
                    decodeTime = ReadDecodeTime(payload);
                    hasDecodeTime = true;
                }
                else if (type == BoxTypes.Trun)
                {
                    TrackRunParser.AppendSamples(
                        payload,
                        ref decodeTime,
                        defaultDuration,
                        defaultSize,
                        defaultFlags,
                        baseOffset, firstRun,
                        ref mediaOffset,
                        samples);
                    firstRun = false;
                }
            }

            if (trackId == 0)
            {
                return null;
            }

            var track = new FragmentTrack(trackId)
            {
                HasDecodeTime = hasDecodeTime,
                DefaultSampleDuration = defaultDuration,
                DefaultSampleSize = defaultSize,
                DefaultSampleFlags = defaultFlags
            };
            track.Samples.AddRange(samples);
            return track;
        }

        private static void ReadTfhdDefaults(
            ReadOnlySpan<byte> payload,
            uint flags,
            out uint defaultDuration,
            out uint defaultSize,
            out uint defaultSampleFlags)
        {
            defaultDuration = 0;
            defaultSize = 0;
            defaultSampleFlags = 0;
            var offset = 8;

            if ((flags & TfhdBaseDataOffsetPresent) != 0)
            {
                offset += 8;
            }

            if ((flags & TfhdSampleDescriptionIndexPresent) != 0)
            {
                offset += 4;
            }

            if ((flags & TfhdDefaultSampleDurationPresent) != 0)
            {
                defaultDuration = ReadUInt32(payload, ref offset);
            }

            if ((flags & TfhdDefaultSampleSizePresent) != 0)
            {
                defaultSize = ReadUInt32(payload, ref offset);
            }

            if ((flags & TfhdDefaultSampleFlagsPresent) != 0)
            {
                defaultSampleFlags = ReadUInt32(payload, ref offset);
            }
        }

        private static uint ReadUInt32(
            ReadOnlySpan<byte> payload,
            ref int offset)
        {
            if (offset > payload.Length - 4)
            {
                throw new Mp4FormatException("Truncated tfhd box.");
            }

            var value = BigEndian.U32(payload[offset..]);
            offset += 4;
            return value;
        }

        private static long ReadDecodeTime(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 4)
                throw new Mp4FormatException("Truncated tfdt box.");
            if (payload[0] > 1)
                throw new Mp4FormatException("Unsupported tfdt version.");
            if (payload.Length < (payload[0] == 1 ? 12 : 8))
                throw new Mp4FormatException("Truncated tfdt decode time.");
            return payload[0] == 1
                ? checked((long)BigEndian.U64(payload[4..]))
                : BigEndian.U32(payload[4..]);
        }
    }

    private static class TrackRunParser
    {
        public static void AppendSamples(
            ReadOnlySpan<byte> payload,
            ref long dts,
            uint defaultDuration,
            uint defaultSize,
            uint defaultFlags,
            long baseOffset, bool firstRun,
            ref int mediaOffset,
            List<FragmentSample> samples)
        {
            if (payload.Length < 8)
                throw new Mp4FormatException("Truncated trun header.");

            var flags = BigEndian.U32(payload[0..]) & 0x00ffffff;
            var count = BigEndian.U32(payload[4..]);
            var offset = 8;

            var runOffset = firstRun ? checked((int)baseOffset) : mediaOffset;
            if ((flags & 1) != 0)
            {
                if (offset > payload.Length - 4)
                {
                    throw new Mp4FormatException("Truncated trun data-offset field.");
                }

                var dataOffset = BigEndian.I32(payload[offset..]);
                offset += 4;
                runOffset = checked((int)(baseOffset + dataOffset));
            }

            var hasDuration = (flags & 0x100) != 0;
            var hasSize = (flags & 0x200) != 0;
            var hasFlags = (flags & 0x400) != 0;
            var hasCompositionOffset = (flags & 0x800) != 0;
            var hasFirstSampleFlags = (flags & 0x4) != 0;
            var firstSampleFlags = hasFirstSampleFlags
                ? ReadFirstSampleFlags(payload, ref flags, ref offset)
                : defaultFlags;
            var sampleOffset = runOffset;

            for (var index = 0; index < count; index++)
            {
                if (!TryReadSample(
                    payload,
                    ref offset,
                    hasDuration,
                    hasSize,
                    hasFlags,
                    hasCompositionOffset,
                    payload[0] == 1,
                    index == 0 ? firstSampleFlags : defaultFlags,
                    defaultDuration,
                    defaultSize,
                    dts,
                    sampleOffset,
                    out var sample,
                    out var duration))
                {
                    return;
                }

                samples.Add(sample);
                dts += duration;
                sampleOffset = checked(sampleOffset + samples[^1].Size);
                mediaOffset = sampleOffset;
            }
        }

        private static uint ReadFirstSampleFlags(
            ReadOnlySpan<byte> payload,
            ref uint flags,
            ref int offset)
        {
            if (offset > payload.Length - 4)
            {
                throw new Mp4FormatException("Truncated trun first-sample-flags field.");
            }

            var value = BigEndian.U32(payload[offset..]);
            offset += 4;
            return value;
        }

        private static bool TryReadSample(
            ReadOnlySpan<byte> payload,
            ref int offset,
            bool hasDuration,
            bool hasSize,
            bool hasFlags,
            bool hasCompositionOffset,
            bool signedCompositionOffset,
            uint defaultFlags,
            uint defaultDuration,
            uint defaultSize,
            long dts,
            int dataOffset,
            out FragmentSample sample,
            out int duration)
        {
            sample = default;
            duration = 0;

            if (!TryReadUInt32(payload, ref offset, hasDuration, defaultDuration, out var durationValue) ||
                !TryReadUInt32(payload, ref offset, hasSize, defaultSize, out var sizeValue) ||
                !TryReadUInt32(payload, ref offset, hasFlags, defaultFlags, out var flagsValue) ||
                !TryReadUInt32(payload, ref offset, hasCompositionOffset, 0, out var compositionOffsetValue))
            {
                return false;
            }

            duration = checked((int)durationValue);
            var compositionOffset = signedCompositionOffset
                ? (long)unchecked((int)compositionOffsetValue) : compositionOffsetValue;
            var size = checked((int)sizeValue);
            var sampleFlags = (flagsValue & 0x00010000) == 0
                ? SampleFlags.KeyFrame
                : SampleFlags.None;

            sample = new FragmentSample(
                size,
                duration,
                dts,
                dts + compositionOffset,
                sampleFlags,
                dataOffset);
            return true;
        }

        private static bool TryReadUInt32(
            ReadOnlySpan<byte> payload,
            ref int offset,
            bool present,
            uint defaultValue,
            out uint value)
        {
            value = defaultValue;
            if (!present)
            {
                return true;
            }

            if (offset > payload.Length - 4)
                throw new Mp4FormatException("Truncated trun sample field.");

            value = BigEndian.U32(payload[offset..]);
            offset += 4;
            return true;
        }

        private static bool TryReadInt32(
            ReadOnlySpan<byte> payload,
            ref int offset,
            bool present,
            out int value)
        {
            value = 0;
            if (!present)
            {
                return true;
            }

            if (offset > payload.Length - 4)
            {
                return false;
            }

            value = BigEndian.I32(payload[offset..]);
            offset += 4;
            return true;
        }
    }
}
