namespace Mp4BoxKit.Boxes;

internal interface IWritableBox
{
    void WriteTo(IsoBoxBuffer buffer);
}
