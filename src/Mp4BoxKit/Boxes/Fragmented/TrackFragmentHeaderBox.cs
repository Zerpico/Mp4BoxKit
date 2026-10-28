namespace Mp4BoxKit.Boxes.Fragmented;

internal sealed class TrackFragmentHeaderBox : IWritableBox
{
    private readonly MediaTrackInfo _track;

    public TrackFragmentHeaderBox(MediaTrackInfo track)
    {
        _track = track;
    }

    public void WriteTo(IsoBoxBuffer buffer)
    {
        IsoBoxWriter.Box(buffer, BoxTypes.Tfhd, header =>
        {
            header.WriteUInt32(IsoBmffConstants.TfhdDefaultBaseIsMoof);
            header.WriteUInt32(_track.Id.Value);
        });
    }
}
