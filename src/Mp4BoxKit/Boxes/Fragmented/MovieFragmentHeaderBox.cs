namespace Mp4BoxKit.Boxes.Fragmented;

internal sealed class MovieFragmentHeaderBox : IWritableBox
{
    private readonly uint _sequence;

    public MovieFragmentHeaderBox(uint sequence)
    {
        _sequence = sequence;
    }

    public void WriteTo(IsoBoxBuffer buffer)
    {
        IsoBoxWriter.Box(buffer, BoxTypes.Mfhd, header =>
        {
            header.WriteUInt32(0);
            header.WriteUInt32(_sequence);
        });
    }
}
