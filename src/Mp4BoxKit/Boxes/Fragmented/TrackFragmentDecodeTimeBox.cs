namespace Mp4BoxKit.Boxes.Fragmented;

internal sealed class TrackFragmentDecodeTimeBox : IWritableBox
{
    private readonly long _decodeTime;

    public TrackFragmentDecodeTimeBox(long decodeTime)
    {
        _decodeTime = decodeTime;
    }

    public void WriteTo(IsoBoxBuffer buffer)
    {
        IsoBoxWriter.Box(buffer, BoxTypes.Tfdt, decodeTime =>
        {
            decodeTime.WriteUInt32(IsoBmffConstants.FullBoxVersionOne);
            decodeTime.WriteUInt64(checked((ulong)_decodeTime));
        });
    }
}
