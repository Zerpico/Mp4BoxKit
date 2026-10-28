using System.Buffers;

namespace Mp4BoxKit.Writer;

internal sealed class FragmentSampleBuffer
{
    private readonly List<List<PendingSample>> _tracks = new();
    public long PayloadBytes { get; private set; }
    public int SampleCount { get; private set; }

    public IReadOnlyList<List<PendingSample>> Tracks => _tracks;
    public bool HasSamples => _tracks.Any(samples => samples.Count != 0);

    public void EnsureTracks(int count)
    {
        while (_tracks.Count < count)
            _tracks.Add(new());
    }

    public IReadOnlyList<PendingSample> GetTrack(TrackId id)
        => _tracks[(int)id.Value - 1];

    public void Add(TrackId id, ReadOnlySpan<byte> data, long pts, long dts,
        SampleFlags flags, int? duration = null)
    {
        var samples = _tracks[(int)id.Value - 1];
        if (samples.Count != 0 && !samples[^1].HasExplicitDuration)
            samples[^1].Duration = checked((int)Math.Max(0, dts - samples[^1].Dts));

        var buffer = ArrayPool<byte>.Shared.Rent(data.Length);
        data.CopyTo(buffer);
        samples.Add(new PendingSample(buffer, data.Length, pts, dts, flags)
        {
            Duration = duration ?? 0,
            HasExplicitDuration = duration.HasValue
        });
        PayloadBytes += data.Length;
        SampleCount++;
    }

    public void AddBorrowed(TrackId id, ReadOnlyMemory<byte> data, long pts, long dts,
        SampleFlags flags, int? duration = null)
    {
        var samples = _tracks[(int)id.Value - 1];
        if (samples.Count != 0 && !samples[^1].HasExplicitDuration)
            samples[^1].Duration = checked((int)Math.Max(0, dts - samples[^1].Dts));

        samples.Add(new PendingSample(data, pts, dts, flags)
        {
            Duration = duration ?? 0,
            HasExplicitDuration = duration.HasValue
        });
        PayloadBytes += data.Length;
        SampleCount++;
    }

    public void FinalizeDurations()
    {
        foreach (var samples in _tracks)
        {
            if (samples.Count != 0 && samples[^1].Duration == 0)
                samples[^1].Duration = samples.Count > 1
                    ? Math.Max(1, samples[^2].Duration) : 1;
        }
    }

    public void Clear()
    {
        foreach (var samples in _tracks)
        {
            foreach (var sample in samples)
                sample.Return();
            samples.Clear();
        }
        PayloadBytes = 0;
        SampleCount = 0;
    }
}
