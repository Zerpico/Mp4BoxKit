using Mp4BoxKit.Boxes;
using Mp4BoxKit.Reader.Metadata;
using Mp4BoxKit.Reader.Classic;

namespace Mp4BoxKit.Reader.Classic;

internal sealed class ClassicSampleTableParser
{
    public List<ClassicTrack> ParseTracks(
        ReadOnlySpan<byte> moov)
    {
        var tracks = new List<ClassicTrack>();
        var offset = 0;

        while (TryReadChild(moov, ref offset, out var type, out var payload))
        {
            if (type == BoxTypes.Trak)
            {
                tracks.Add(ParseClassicTrack(payload));
            }
        }

        return tracks;
    }

    private static ClassicTrack ParseClassicTrack(
        ReadOnlySpan<byte> trak)
    {
        var metadata = Mp4MetadataParser.ParseTrackMetadata(trak);
        var track = new ClassicTrack
        {
            Track = Mp4MetadataParser.CreateTrackInfo(metadata)
        };

        var offset = 0;
        while (TryReadChild(trak, ref offset, out var type, out var payload))
        {
            if (type == BoxTypes.Mdia)
            {
                ParseClassicMedia(payload, track);
            }
        }

        return track;
    }

    private static void ParseClassicMedia(
        ReadOnlySpan<byte> mdia,
        ClassicTrack track)
    {
        var offset = 0;
        while (TryReadChild(mdia, ref offset, out var type, out var payload))
        {
            if (type == BoxTypes.Minf)
            {
                ParseClassicMinf(payload, track);
            }
        }
    }

    private static void ParseClassicMinf(
        ReadOnlySpan<byte> minf,
        ClassicTrack track)
    {
        var offset = 0;
        while (TryReadChild(minf, ref offset, out var type, out var payload))
        {
            if (type == BoxTypes.Stbl)
            {
                ParseClassicSampleTable(payload, track);
            }
        }
    }

    private static void ParseClassicSampleTable(
        ReadOnlySpan<byte> stbl,
        ClassicTrack track)
    {
        var offset = 0;
        while (TryReadChild(stbl, ref offset, out var type, out var payload))
        {
            switch (type)
            {
                case var value when value == BoxTypes.Stts:
                    ParseTimeToSample(payload, track);
                    break;
                case var value when value == BoxTypes.Ctts:
                    ParseCompositionOffsets(payload, track);
                    break;
                case var value when value == BoxTypes.Stsc:
                    ParseSampleToChunk(payload, track);
                    break;
                case var value when value == BoxTypes.Stsz:
                    ParseSampleSizes(payload, track);
                    break;
                case var value when value == BoxTypes.Stco:
                    ParseChunkOffsets(payload, track, false);
                    break;
                case var value when value == BoxTypes.Co64:
                    ParseChunkOffsets(payload, track, true);
                    break;
                case var value when value == BoxTypes.Stss:
                    ParseSyncSamples(payload, track);
                    break;
            }
        }
    }

    private static void ParseTimeToSample(
        ReadOnlySpan<byte> payload,
        ClassicTrack track)
    {
        if (payload.Length < 8)
        {
            return;
        }

        var count = BigEndian.U32(payload[4..]);
        var offset = 8;
        for (var i = 0; i < count && offset + 8 <= payload.Length; i++)
        {
            track.TimeRuns.Add((
                BigEndian.U32(payload[offset..]),
                BigEndian.U32(payload[(offset + 4)..])));
            offset += 8;
        }
    }

    private static void ParseCompositionOffsets(
        ReadOnlySpan<byte> payload,
        ClassicTrack track)
    {
        if (payload.Length < 8)
        {
            return;
        }

        var version = payload[0];
        var count = BigEndian.U32(payload[4..]);
        var offset = 8;
        for (var i = 0; i < count && offset + 8 <= payload.Length; i++)
        {
            var value = version == 1
                ? BigEndian.I32(payload[(offset + 4)..])
                : checked((int)BigEndian.U32(payload[(offset + 4)..]));
            track.CompositionRuns.Add((BigEndian.U32(payload[offset..]), value));
            offset += 8;
        }
    }

    private static void ParseSampleToChunk(
        ReadOnlySpan<byte> payload,
        ClassicTrack track)
    {
        if (payload.Length < 8)
        {
            return;
        }

        var count = BigEndian.U32(payload[4..]);
        var offset = 8;
        for (var i = 0; i < count && offset + 12 <= payload.Length; i++)
        {
            track.ChunkRuns.Add((
                BigEndian.U32(payload[offset..]),
                BigEndian.U32(payload[(offset + 4)..])));
            offset += 12;
        }
    }

    private static void ParseSampleSizes(
        ReadOnlySpan<byte> payload,
        ClassicTrack track)
    {
        if (payload.Length < 12)
        {
            return;
        }

        var uniformSize = BigEndian.U32(payload[4..]);
        var count = BigEndian.U32(payload[8..]);
        var offset = 12;
        for (var i = 0; i < count; i++)
        {
            var size = uniformSize != 0
                ? uniformSize
                : offset + 4 <= payload.Length
                    ? BigEndian.U32(payload[offset..])
                    : 0;
            track.SampleSizes.Add(size);
            if (uniformSize == 0)
            {
                offset += 4;
            }
        }
    }

    private static void ParseChunkOffsets(
        ReadOnlySpan<byte> payload,
        ClassicTrack track,
        bool wide)
    {
        if (payload.Length < 8)
        {
            return;
        }

        var count = BigEndian.U32(payload[4..]);
        var offset = 8;
        var width = wide ? 8 : 4;
        for (var i = 0; i < count && offset + width <= payload.Length; i++)
        {
            track.ChunkOffsets.Add(wide
                ? BigEndian.U64(payload[offset..])
                : BigEndian.U32(payload[offset..]));
            offset += width;
        }
    }

    private static void ParseSyncSamples(
        ReadOnlySpan<byte> payload,
        ClassicTrack track)
    {
        if (payload.Length < 8)
        {
            return;
        }

        var count = BigEndian.U32(payload[4..]);
        var offset = 8;
        for (var i = 0; i < count && offset + 4 <= payload.Length; i++)
        {
            track.SyncSamples.Add(BigEndian.U32(payload[offset..]));
            offset += 4;
        }
    }

    public List<ClassicSample> BuildSamples(ClassicTrack track)
    {
        var result = new List<ClassicSample>(track.SampleSizes.Count);
        var sampleIndex = 0;
        var dts = 0L;

        for (var chunkIndex = 0; chunkIndex < track.ChunkOffsets.Count; chunkIndex++)
        {
            var chunkNumber = (uint)chunkIndex + 1;
            var run = FindChunkRun(track.ChunkRuns, chunkNumber);
            var sampleOffset = checked((long)track.ChunkOffsets[chunkIndex]);

            for (var i = 0; i < run; i++)
            {
                if (sampleIndex >= track.SampleSizes.Count)
                {
                    break;
                }

                var size = checked((int)track.SampleSizes[sampleIndex]);
                var duration = DurationAt(track.TimeRuns, sampleIndex);
                var compositionOffset = OffsetAt(track.CompositionRuns, sampleIndex);
                var sampleNumber = (uint)sampleIndex + 1;
                var flags = track.SyncSamples.Count == 0 ||
                    track.SyncSamples.Contains(sampleNumber)
                    ? SampleFlags.KeyFrame
                    : SampleFlags.None;

                result.Add(new ClassicSample(
                    sampleOffset,
                    size,
                    dts,
                    dts + compositionOffset,
                    duration,
                    flags));

                sampleOffset += size;
                dts += duration;
                sampleIndex++;
            }
        }

        return result;
    }

    private static int FindChunkRun(
        List<(uint FirstChunk, uint SamplesPerChunk)> runs,
        uint chunk)
    {
        for (var i = runs.Count - 1; i >= 0; i--)
        {
            if (chunk >= runs[i].FirstChunk)
            {
                return checked((int)runs[i].SamplesPerChunk);
            }
        }

        return 0;
    }

    private static int DurationAt(
        List<(uint Count, uint Duration)> runs,
        int sampleIndex)
    {
        foreach (var run in runs)
        {
            if (sampleIndex < run.Count)
            {
                return checked((int)run.Duration);
            }

            sampleIndex -= checked((int)run.Count);
        }

        return 0;
    }

    private static int OffsetAt(
        List<(uint Count, int Offset)> runs,
        int sampleIndex)
    {
        foreach (var run in runs)
        {
            if (sampleIndex < run.Count)
            {
                return run.Offset;
            }

            sampleIndex -= checked((int)run.Count);
        }

        return 0;
    }

    public (int Offset, int Length) FindTopLevelBox(
        ReadOnlySpan<byte> file,
        FourCc wanted)
    {
        var offset = 0;
        while (TryReadChild(file, ref offset, out var type, out var payload))
        {
            if (type == wanted)
            {
                return (offset - payload.Length, payload.Length);
            }
        }

        return default;
    }

    private static bool TryReadChild(
        ReadOnlySpan<byte> data,
        ref int offset,
        out FourCc type,
        out ReadOnlySpan<byte> payload)
    {
        var reader = new IsoBoxReader(data, offset);
        var result = reader.TryRead(out type, out payload);
        offset = reader.Position;
        return result;
    }

}
