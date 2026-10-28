namespace Mp4BoxKit.Reader.Fragments;

internal sealed class FragmentTrack(uint id)
{
    public uint TrackId = id;
    public uint TimeScale = 1;
    public uint DefaultSampleDuration;
    public uint DefaultSampleSize;
    public uint DefaultSampleFlags;
    public bool HasDecodeTime;
    public readonly List<FragmentSample> Samples = new();
}

internal sealed class FragmentReaderState
{
    public List<FragmentTrack> Tracks { get; } = new();
    public byte[] Media { get; set; } = Array.Empty<byte>();
    public int MediaOffset { get; set; }
    public int TrackIndex { get; set; }
    public int SampleIndex { get; set; }
    public List<int> SampleIndices { get; } = new();

    public void ResetSamples()
    {
        SampleIndices.Clear();
        TrackIndex = 0;
        SampleIndex = 0;
        MediaOffset = 0;
    }
}

internal readonly record struct FragmentSample(
    int Size,
    int Duration,
    long Dts,
    long Pts,
    SampleFlags Flags,
    int DataOffset);
