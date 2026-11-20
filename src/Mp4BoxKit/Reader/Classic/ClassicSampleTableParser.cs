using Mp4BoxKit.Boxes;
using Mp4BoxKit.Reader.Metadata;

namespace Mp4BoxKit.Reader.Classic;

internal sealed class ClassicSampleTableParser(Mp4ProbeOptions options)
{
    public List<ClassicTrack> ParseTracks(
        ReadOnlySpan<byte> moov)
    {
        var tracks = new List<ClassicTrack>();
        var budget = new SampleMetadataBudget(options.MaxMetadataBytes);
        var offset = 0;

        while (TryReadChild(moov, ref offset, out var type, out var payload))
        {
            if (type == BoxTypes.Trak)
            {
                tracks.Add(ParseClassicTrack(payload, budget));
            }
        }

        return tracks;
    }

    private static ClassicTrack ParseClassicTrack(ReadOnlySpan<byte> trak, SampleMetadataBudget budget)
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
                ParseClassicMedia(payload, track, budget);
            }
        }

        ValidateTrack(track);
        return track;
    }

    private static void ParseClassicMedia(ReadOnlySpan<byte> mdia, ClassicTrack track, SampleMetadataBudget budget)
    {
        var offset = 0;
        while (TryReadChild(mdia, ref offset, out var type, out var payload))
        {
            if (type == BoxTypes.Minf)
            {
                ParseClassicMinf(payload, track, budget);
            }
        }
    }

    private static void ParseClassicMinf(ReadOnlySpan<byte> minf, ClassicTrack track, SampleMetadataBudget budget)
    {
        var offset = 0;
        while (TryReadChild(minf, ref offset, out var type, out var payload))
        {
            if (type == BoxTypes.Stbl)
            {
                ParseClassicSampleTable(payload, track, budget);
            }
        }
    }

    private static void ParseClassicSampleTable(ReadOnlySpan<byte> stbl, ClassicTrack track, SampleMetadataBudget budget)
    {
        var offset = 0;
        uint seen = 0;
        while (TryReadChild(stbl, ref offset, out var type, out var payload))
        {
            uint table = type == BoxTypes.Stts ? 1u : type == BoxTypes.Ctts ? 2u
                : type == BoxTypes.Stsc ? 4u : type == BoxTypes.Stsz ? 8u
                : type == BoxTypes.Stco || type == BoxTypes.Co64 ? 16u
                : type == BoxTypes.Stss ? 32u : 0u;
            if ((seen & table) != 0)
                throw new Mp4FormatException("Duplicate classic sample table.");
            seen |= table;
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
                    ParseSampleSizes(payload, track, budget);
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

    private static void ParseTimeToSample(ReadOnlySpan<byte> payload, ClassicTrack track)
    {
        var count = ValidateTable(payload, 8, "stts");
        var offset = 8;
        for (var i = 0; i < count; i++)
        {
            track.TimeRuns.Add((
                BigEndian.U32(payload[offset..]),
                BigEndian.U32(payload[(offset + 4)..])));
            offset += 8;
        }
    }

    private static void ParseCompositionOffsets(ReadOnlySpan<byte> payload, ClassicTrack track)
    {
        var count = ValidateTable(payload, 8, "ctts");
        if (payload[0] > 1)
            throw new Mp4FormatException("Unsupported ctts version.");
        track.HasCompositionOffsets = true;
        var version = payload[0];
        var offset = 8;
        for (var i = 0; i < count; i++)
        {
            var value = version == 1
                ? BigEndian.I32(payload[(offset + 4)..])
                : checked((int)BigEndian.U32(payload[(offset + 4)..]));
            track.CompositionRuns.Add((BigEndian.U32(payload[offset..]), value));
            offset += 8;
        }
    }

    private static void ParseSampleToChunk(ReadOnlySpan<byte> payload, ClassicTrack track)
    {
        var count = ValidateTable(payload, 12, "stsc");
        var offset = 8;
        for (var i = 0; i < count; i++)
        {
            if (BigEndian.U32(payload[(offset + 8)..]) != 1)
                throw new Mp4FormatException("Unsupported stsc sample description index.");
            track.ChunkRuns.Add((
                BigEndian.U32(payload[offset..]),
                BigEndian.U32(payload[(offset + 4)..])));
            offset += 12;
        }
    }

    private static void ParseSampleSizes(ReadOnlySpan<byte> payload, ClassicTrack track, SampleMetadataBudget budget)
    {
        if (payload.Length < 12)
        {
            throw new Mp4FormatException("Truncated stsz header.");
        }

        var uniformSize = BigEndian.U32(payload[4..]);
        var count = BigEndian.U32(payload[8..]);
        if (uniformSize == 0 && count > (payload.Length - 12) / 4)
            throw new Mp4FormatException("Truncated stsz sample sizes.");

        budget.Reserve(count, SampleMetadataBudget.ClassicSampleBytes);
        var offset = 12;
        for (var i = 0; i < count; i++)
        {
            var size = uniformSize != 0
                ? uniformSize
                : BigEndian.U32(payload[offset..]);
            track.SampleSizes.Add(size);
            if (uniformSize == 0)
            {
                offset += 4;
            }
        }
    }

    private static void ParseChunkOffsets(ReadOnlySpan<byte> payload, ClassicTrack track, bool wide)
    {
        var width = wide ? 8 : 4;
        var count = ValidateTable(payload, width, wide ? "co64" : "stco");
        var offset = 8;
        for (var i = 0; i < count; i++)
        {
            track.ChunkOffsets.Add(wide
                ? BigEndian.U64(payload[offset..])
                : BigEndian.U32(payload[offset..]));
            offset += width;
        }
    }

    private static void ParseSyncSamples(ReadOnlySpan<byte> payload, ClassicTrack track)
    {
        var count = ValidateTable(payload, 4, "stss");
        track.HasSyncSamples = true;
        var offset = 8;
        uint previous = 0;
        for (var i = 0; i < count; i++)
        {
            var number = BigEndian.U32(payload[offset..]);
            if (number <= previous)
                throw new Mp4FormatException("Invalid stss sample order.");
            track.SyncSamples.Add(number);
            previous = number;
            offset += 4;
        }
    }

    public List<ClassicSample> BuildSamples(ClassicTrack track)
    {
        var result = new List<ClassicSample>(track.SampleSizes.Count);
        var cursor = new ClassicSampleCursor(track);
        while (cursor.TryRead(out var sample))
            result.Add(sample);
        return result;
    }

    private static uint ValidateTable(ReadOnlySpan<byte> payload, int width, string name)
    {
        if (payload.Length < 8)
            throw new Mp4FormatException($"Truncated {name} header.");
        var count = BigEndian.U32(payload[4..]);
        if (count > (payload.Length - 8) / width)
            throw new Mp4FormatException($"Truncated {name} entries.");
        return count;
    }

    private static void ValidateTrack(ClassicTrack track)
    {
        var samples = (ulong)track.SampleSizes.Count;
        ulong timed = 0;
        foreach (var run in track.TimeRuns)
        {
            if (run.Count == 0)
                throw new Mp4FormatException("Empty stts run.");
            timed += run.Count;
        }
        if (timed != samples)
            throw new Mp4FormatException("stts does not cover the declared samples.");

        ulong composed = 0;
        foreach (var run in track.CompositionRuns)
        {
            if (run.Count == 0)
                throw new Mp4FormatException("Empty ctts run.");
            composed += run.Count;
        }
        if (track.HasCompositionOffsets && composed != samples)
            throw new Mp4FormatException("ctts does not cover the declared samples.");

        ulong mapped = 0;
        var chunks = (uint)track.ChunkOffsets.Count;
        for (var i = 0; i < track.ChunkRuns.Count; i++)
        {
            var run = track.ChunkRuns[i];
            var next = i + 1 < track.ChunkRuns.Count ? track.ChunkRuns[i + 1].FirstChunk : chunks + 1;
            if ((i == 0 && run.FirstChunk != 1) || run.FirstChunk == 0 ||
                run.FirstChunk > chunks || next <= run.FirstChunk || next > chunks + 1 ||
                run.SamplesPerChunk == 0)
                throw new Mp4FormatException("Invalid stsc chunk mapping.");
            mapped += (ulong)(next - run.FirstChunk) * run.SamplesPerChunk;
        }
        if (mapped != samples || (chunks != 0 && track.ChunkRuns.Count == 0))
            throw new Mp4FormatException("Chunk mapping does not cover the declared samples.");
        foreach (var number in track.SyncSamples)
            if (number > samples)
                throw new Mp4FormatException("stss sample number exceeds sample count.");
    }

    public (int Offset, int Length) FindTopLevelBox(ReadOnlySpan<byte> file, FourCc wanted)
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
