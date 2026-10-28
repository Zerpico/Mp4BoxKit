namespace Mp4BoxKit.Boxes;

internal static class FileTypeWriter
{
    public static byte[] Serialize()
    {
        var buffer = new IsoBoxBuffer();
        IsoBoxWriter.Box(buffer, BoxTypes.Ftyp, box =>
        {
            box.WriteUInt32(new FourCc("isom").Value);
            box.WriteUInt32(IsoBmffConstants.FileTypeMinorVersion);
            box.WriteUInt32(new FourCc("isom").Value);
            box.WriteUInt32(new FourCc("iso2").Value);
            box.WriteUInt32(new FourCc("mp41").Value);
        });
        return buffer.ToArray();
    }

    /// <summary>Segment Type Box for independently delivered media segments.</summary>
    public static byte[] SerializeSegmentType()
    {
        var buffer = new IsoBoxBuffer();
        IsoBoxWriter.Box(buffer, BoxTypes.Styp, box =>
        {
            box.WriteUInt32(new FourCc("msdh").Value);
            box.WriteUInt32(0);
            box.WriteUInt32(new FourCc("msdh").Value);
            box.WriteUInt32(new FourCc("msix").Value);
            box.WriteUInt32(new FourCc("iso6").Value);
            box.WriteUInt32(new FourCc("iso5").Value);
            box.WriteUInt32(new FourCc("isom").Value);
        });
        return buffer.ToArray();
    }
}
