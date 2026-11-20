using Mp4BoxKit.Writer;

namespace Mp4BoxKit.Boxes.Fragmented;

internal sealed class TrackRunBox : IWritableBox
{
    private readonly MediaTrackInfo _track;
    private readonly List<PendingSample> _samples;
    private readonly int _dataOffset;

    public TrackRunBox(
        MediaTrackInfo track,
        List<PendingSample> samples,
        int dataOffset)
    {
        _track = track;
        _samples = samples;
        _dataOffset = dataOffset;
    }

    public void WriteTo(IsoBoxBuffer buffer)
    {
        IsoBoxWriter.Box(buffer, BoxTypes.Trun, run =>
        {
            var signedCompositionOffsets = false;
            foreach (var sample in _samples)
            {
                if (sample.Pts < sample.Dts)
                {
                    signedCompositionOffsets = true;
                    break;
                }
            }

            run.WriteUInt32((signedCompositionOffsets
                ? IsoBmffConstants.FullBoxVersionOne : 0)
                | IsoBmffConstants.TrunFields);
            run.WriteUInt32((uint)_samples.Count);
            run.WriteInt32(_dataOffset);

            foreach (var sample in _samples)
            {
                run.WriteUInt32((uint)sample.Duration);
                run.WriteUInt32((uint)sample.Data.Length);
                run.WriteUInt32(IsSync(sample) ? 0u : IsoBmffConstants.NonSyncSample);
                run.WriteInt32(checked((int)(sample.Pts - sample.Dts)));
            }
        });
    }

    // Only video has non-sync samples; audio and subtitle samples are always sync,
    // as in the classic writer, which emits stss for video tracks only.
    private bool IsSync(PendingSample sample) =>
        _track is not VideoTrackInfo || (sample.Flags & SampleFlags.KeyFrame) != 0;
}
