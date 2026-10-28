namespace Mp4BoxKit.Boxes;

internal static class MediaInformationWriter
{
    public static void WriteMediaHeader(IsoBoxBuffer buffer, MediaType type)
    {
        IsoBoxWriter.Box(buffer, MovieHeaderWriter.GetMediaHeaderType(type), box =>
        {
            switch (type)
            {
                case MediaType.Video:
                    box.WriteUInt32(1); // version 0, flags 1
                    box.WriteUInt16(0); // graphicsmode
                    box.WriteUInt16(0); // opcolor red
                    box.WriteUInt16(0); // opcolor green
                    box.WriteUInt16(0); // opcolor blue
                    break;
                case MediaType.Audio:
                    box.WriteUInt32(0); // version 0, flags 0
                    box.WriteUInt16(0); // balance
                    box.WriteUInt16(0); // reserved
                    break;
                default:
                    box.WriteUInt32(0); // nmhd: version/flags only
                    break;
            }
        });
    }

    public static void WriteDataInformation(IsoBoxBuffer buffer)
    {
        IsoBoxWriter.Box(buffer, BoxTypes.Dinf, dinf =>
        {
            IsoBoxWriter.Box(dinf, BoxTypes.Dref, dref =>
            {
                dref.WriteUInt32(0); // version/flags
                dref.WriteUInt32(1); // entry_count
                IsoBoxWriter.Box(dref, BoxTypes.Url, url =>
                {
                    url.WriteUInt32(1); // self-contained
                });
            });
        });
    }
}
