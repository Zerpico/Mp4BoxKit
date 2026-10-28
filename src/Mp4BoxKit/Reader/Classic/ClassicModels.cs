namespace Mp4BoxKit.Reader.Classic;

internal sealed class ClassicTrack
{
    public required MediaTrackInfo Track { get; init; }
    public readonly List<(uint Count, uint Duration)> TimeRuns = new();
    public readonly List<(uint Count, int Offset)> CompositionRuns = new();
    public readonly List<(uint FirstChunk, uint SamplesPerChunk)> ChunkRuns = new();
    public readonly List<uint> SampleSizes = new();
    public readonly List<ulong> ChunkOffsets = new();
    public readonly HashSet<uint> SyncSamples = new();
}

internal sealed class ClassicReaderState
{
    public readonly List<ClassicIndexedSample> Samples = new();
    public int NextSampleIndex;
}

internal readonly record struct ClassicIndexedSample(
    TrackId Track,
    uint TimeScale,
    long Offset,
    int Size,
    long Dts,
    long Pts,
    int Duration,
    SampleFlags Flags);

internal readonly record struct ClassicSample(
    long Offset,
    int Size,
    long Dts,
    long Pts,
    int Duration,
    SampleFlags Flags);
