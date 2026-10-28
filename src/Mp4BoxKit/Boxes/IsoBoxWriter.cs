namespace Mp4BoxKit.Boxes;

internal static class IsoBoxWriter
{
    public static void Box(
        IsoBoxBuffer buffer,
        FourCc type,
        Action<IsoBoxBuffer> body)
    {
        var position = buffer.BeginBox(type);
        body(buffer);
        buffer.EndBox(position);
    }
}
