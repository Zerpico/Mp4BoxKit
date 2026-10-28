namespace Mp4BoxKit.Reader.Fragments;

internal sealed class FragmentSeeker(Stream stream, InputBuffer buffer, FragmentLoader loader)
{
    private readonly SegmentIndexReader _indexes = new(new ContainerInput(buffer));
    public async ValueTask<FragmentReaderState?> SeekAsync(double seconds,
        TrackId? track, IReadOnlyList<MediaTrackInfo> tracks, CancellationToken cancellationToken)
    {
        if (!stream.CanSeek)
            throw new NotSupportedException("Seeking fragmented MP4 requires a seekable stream.");
        var selected = track is { } id ? tracks.First(t => t.Id == id)
            : tracks.FirstOrDefault(t => t is VideoTrackInfo) ?? tracks.First();
        var video = selected is VideoTrackInfo;
        var indexedOffset = await _indexes.FindAsync(
                selected.Id.Value,
                selected.TimeScale,
                seconds,
                cancellationToken)
            .ConfigureAwait(false);
        stream.Position = indexedOffset ?? 0;
        buffer.ResetPosition(stream.Position);
        FragmentReaderState? candidate = null;
        long continuation = 0;
        double anchor = 0;
        int selectedIndex = 0;
        while (await loader.ReadNextAsync(tracks, cancellationToken).ConfigureAwait(false) is { } next)
        {
            var item = next.Tracks.FirstOrDefault(t => t.TrackId == selected.Id.Value);
            if (item is null)
            {
                continue;
            }

            for (var i = 0; i < item.Samples.Count; i++)
            {
                var sample = item.Samples[i];
                var time = (double)sample.Pts / Math.Max(1, item.TimeScale);
                if (time <= seconds && (!video || (sample.Flags & SampleFlags.KeyFrame) != 0))
                {
                    candidate = next;
                    continuation = stream.Position;
                    anchor = (double)sample.Dts / Math.Max(1, item.TimeScale);
                    selectedIndex = i;
                }
            }
            if (item.Samples.Count > 0 && (double)item.Samples[^1].Dts / Math.Max(1, item.TimeScale) >= seconds)
            {
                if (candidate is null)
                {
                    candidate = next;
                    continuation = stream.Position;
                    selectedIndex = 0;
                    anchor = (double)item.Samples[0].Dts / Math.Max(1, item.TimeScale);
                }
                break;
            }
        }
        if (candidate is null)
        {
            return null;
        }
        stream.Position = continuation;
        buffer.ResetPosition(stream.Position);
        candidate.SampleIndices.Clear();
        foreach (var item in candidate.Tracks)
        {
            var index = 0;
            if (item.TrackId == selected.Id.Value)
            {
                index = selectedIndex;
            }
            else
            {
                while (index < item.Samples.Count &&
                    (double)(item.Samples[index].Dts +
                        item.Samples[index].Duration) /
                        Math.Max(1, item.TimeScale) <= anchor)
                {
                    index++;
                }
            }
            candidate.SampleIndices.Add(index);
        }
        return candidate;
    }
}
