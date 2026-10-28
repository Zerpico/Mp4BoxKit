namespace Mp4BoxKit.Writer;

/// <summary>Accumulates per-track sample metadata for the final classic movie box.</summary>
internal sealed class ClassicSampleTableBuilder
{
    private readonly List<List<FileSample>> _tracks = new();

    public IReadOnlyList<List<FileSample>> Tracks => _tracks;

    public void Add(int trackCount, TrackId track, byte[] data,
        long pts, long dts, SampleFlags flags, long offset, int? duration = null, int? size = null)
    {
        while (_tracks.Count < trackCount)
            _tracks.Add(new());

        var samples = _tracks[(int)track.Value - 1];
        if (samples.Count != 0 && !samples[^1].HasExplicitDuration)
            samples[^1].Duration = Math.Max(1, dts - samples[^1].Dts);
        samples.Add(new FileSample(data, pts, dts, flags, offset)
        { Duration = duration ?? 0, HasExplicitDuration = duration.HasValue, Size = size ?? data.Length });
    }

    public void FinalizeDurations()
    {
        foreach (var samples in _tracks)
        {
            if (samples.Count != 0 && samples[^1].Duration == 0)
                samples[^1].Duration = samples.Count > 1 ? samples[^2].Duration : 1;
        }
    }
}
