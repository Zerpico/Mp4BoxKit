namespace Mp4BoxKit.Reader.Classic;

internal sealed class ClassicSampleIndexBuilder(ClassicSampleTableParser tables)
{
    public ClassicReaderState Build(ReadOnlySpan<byte> moov)
    {
        var state = new ClassicReaderState();
        var tracks = tables.ParseTracks(moov);
        var sampleCount = 0;
        foreach (var track in tracks)
            sampleCount = checked(sampleCount + track.SampleSizes.Count);
        state.Samples.EnsureCapacity(sampleCount);

        foreach (var track in tracks)
        {
            var cursor = new ClassicSampleCursor(track);
            while (cursor.TryRead(out var sample))
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
