namespace Mp4BoxKit.Reader.Classic;

internal sealed class ClassicSampleSeeker
{
    public void Seek(ClassicReaderState state, double seconds, TrackId? track)
    {
        var samples = state.Samples;
        var selectedTrack = track ?? samples.FirstOrDefault(s => (s.Flags & SampleFlags.KeyFrame) != 0).Track;
        var index = -1;
        for (var i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            if (selectedTrack is { } selected && sample.Track != selected)
                continue;
            if ((double)sample.Pts / Math.Max(1, sample.TimeScale) <= seconds &&
                (sample.Flags & SampleFlags.KeyFrame) != 0)
                index = i;
        }

        // EOF is a valid cursor position, not an index into the sample list.
        state.NextSampleIndex = index < 0 ? 0 : index;
    }
}
