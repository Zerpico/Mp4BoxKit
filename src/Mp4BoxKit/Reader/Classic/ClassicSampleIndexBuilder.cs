namespace Mp4BoxKit.Reader.Classic;

internal sealed class ClassicSampleIndexBuilder(ClassicSampleTableParser tables)
{
    public ClassicReaderState Build(ReadOnlySpan<byte> moov)
    {
        var state = new ClassicReaderState();
        foreach (var track in tables.ParseTracks(moov))
        {
            foreach (var sample in tables.BuildSamples(track))
            {
                state.Samples.Add(new ClassicIndexedSample(
                    track.Track.Id, track.Track.TimeScale,
                    sample.Offset, sample.Size, sample.Dts, sample.Pts,
                    sample.Duration, sample.Flags));
            }
        }

        state.Samples.Sort(static (left, right) =>
        {
            var leftTime = (decimal)left.Dts / Math.Max(1, left.TimeScale);
            var rightTime = (decimal)right.Dts / Math.Max(1, right.TimeScale);
            return leftTime.CompareTo(rightTime);
        });
        return state;
    }
}
