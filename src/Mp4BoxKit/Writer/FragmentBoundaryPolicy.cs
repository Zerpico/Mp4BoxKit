namespace Mp4BoxKit.Writer;

internal sealed class FragmentBoundaryPolicy(Mp4FragmentedWriterOptions options)
{
    public bool ShouldFlush(FragmentSampleBuffer buffer,
        IReadOnlyList<MediaTrackInfo> tracks, TrackId track, long dts, SampleFlags flags)
    {
        if (!buffer.HasSamples || track.Value == 0 || track.Value > tracks.Count)
            return false;

        if ((flags & SampleFlags.SegmentDisable) != 0)
            return false;
        if ((flags & SampleFlags.SegmentForce) != 0)
            return true;
        if (options.FragmentBoundary == Fmp4FragmentBoundary.Manual)
            return false;

        var info = tracks[(int)track.Value - 1];
        if (info.Type == MediaType.Video && (flags & SampleFlags.KeyFrame) != 0)
        {
            if (options.FragmentBoundary == Fmp4FragmentBoundary.EveryKeyFrame ||
                options.TargetFragmentDuration is not { } videoTarget)
                return true;

            var videoSamples = buffer.GetTrack(track);
            if (videoSamples.Count == 0)
                return false;
            var videoScale = Math.Max(1u, info.TimeScale);
            return dts - videoSamples[0].Dts >= videoTarget.TotalSeconds * videoScale;
        }

        // If a video track exists, video keyframes own fragment boundaries.
        if (tracks.Any(item => item.Type == MediaType.Video))
            return false;

        var samples = buffer.GetTrack(track);
        if (samples.Count == 0)
            return false;
        var target = options.TargetFragmentDuration ?? TimeSpan.FromSeconds(2);
        var scale = Math.Max(1u, info.TimeScale);
        return dts - samples[0].Dts >= target.TotalSeconds * scale;
    }
}
