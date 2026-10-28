using Mp4BoxKit.Writer;

namespace Mp4BoxKit.Boxes.Fragmented;

internal sealed class TrackFragmentBox : IWritableBox
{
    private readonly MediaTrackInfo _track;
    private readonly List<PendingSample> _samples;
    private readonly int _dataOffset;

    public TrackFragmentBox(
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
        IsoBoxWriter.Box(buffer, BoxTypes.Traf, traf =>
        {
            new TrackFragmentHeaderBox(_track).WriteTo(traf);
            new TrackFragmentDecodeTimeBox(_samples[0].Dts).WriteTo(traf);
            new TrackRunBox(_samples, _dataOffset).WriteTo(traf);
        });
    }
}
