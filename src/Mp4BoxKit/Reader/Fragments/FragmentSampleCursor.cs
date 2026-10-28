namespace Mp4BoxKit.Reader.Fragments;

/// <summary>Schedules samples from an already loaded fragment in decode-time order.</summary>
internal sealed class FragmentSampleCursor
{
    public bool TryRead(FragmentReaderState state, out Mp4Sample? sample)
    {
        var selectedTrackIndex = -1;
        var selectedTime = decimal.MaxValue;
        for (var index = 0; index < state.Tracks.Count; index++)
        {
            var track = state.Tracks[index];
            var sampleIndex = state.SampleIndices.Count > index
                ? state.SampleIndices[index] : 0;
            if (sampleIndex >= track.Samples.Count)
                continue;

            var time = (decimal)track.Samples[sampleIndex].Dts /
                Math.Max(1, track.TimeScale);
            if (time < selectedTime)
            {
                selectedTime = time;
                selectedTrackIndex = index;
            }
        }

        if (selectedTrackIndex < 0)
        {
            sample = null;
            return false;
        }

        while (state.SampleIndices.Count <= selectedTrackIndex)
            state.SampleIndices.Add(0);

        var selectedTrack = state.Tracks[selectedTrackIndex];
        var fragmentSample = selectedTrack.Samples[
            state.SampleIndices[selectedTrackIndex]++];
        var sampleOffset = fragmentSample.DataOffset;
        if (sampleOffset < 0 || fragmentSample.Size < 0 ||
            sampleOffset > state.Media.Length - fragmentSample.Size)
        {
            throw new Mp4FormatException("trun sample exceeds mdat payload.");
        }

        var info = new SampleInfo(
            new TrackId(selectedTrack.TrackId), fragmentSample.Pts,
            fragmentSample.Dts, fragmentSample.Duration, fragmentSample.Flags);
        sample = new Mp4Sample(info,
            state.Media.AsSpan(sampleOffset, fragmentSample.Size));
        state.MediaOffset = Math.Max(state.MediaOffset,
            checked(sampleOffset + fragmentSample.Size));
        return true;
    }
}
