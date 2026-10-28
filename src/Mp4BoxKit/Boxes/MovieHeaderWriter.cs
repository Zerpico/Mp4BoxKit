namespace Mp4BoxKit.Boxes;

internal static class MovieHeaderWriter
{
    internal static void WriteMovieHeader(IsoBoxBuffer buffer)
        => WriteMovieHeader(buffer, 0, 1);

    internal static void WriteMovieHeader(IsoBoxBuffer buffer, uint duration, uint nextTrackId)
    {
        buffer.WriteUInt32(0);
        buffer.WriteUInt32(0);
        buffer.WriteUInt32(0);
        buffer.WriteUInt32(1000);
        buffer.WriteUInt32(duration);
        buffer.WriteUInt32(IsoBmffConstants.FixedPointOne);
        buffer.WriteUInt16(IsoBmffConstants.AudioTrackVolume);
        buffer.WriteUInt16(0);
        buffer.WriteUInt64(0);
        WriteIdentityMatrix(buffer);
        for (var i = 0; i < 6; i++)
            buffer.WriteUInt32(0);
        buffer.WriteUInt32(nextTrackId);
    }

    internal static void WriteIdentityMatrix(IsoBoxBuffer buffer)
    {
        buffer.WriteUInt32(IsoBmffConstants.FixedPointOne);
        buffer.WriteUInt32(0);
        buffer.WriteUInt32(0);
        buffer.WriteUInt32(0);
        buffer.WriteUInt32(IsoBmffConstants.FixedPointOne);
        buffer.WriteUInt32(0);
        buffer.WriteUInt32(0);
        buffer.WriteUInt32(0);
        buffer.WriteUInt32(IsoBmffConstants.IdentityMatrixLast);
    }

    internal static uint GetHandlerType(MediaType type) => new FourCc(type switch
    {
        MediaType.Audio => "soun",
        MediaType.Subtitle => "subt",
        _ => "vide"
    }).Value;

    internal static FourCc GetMediaHeaderType(MediaType type) => new(type switch
    {
        MediaType.Video => "vmhd",
        MediaType.Audio => "smhd",
        _ => "nmhd"
    });
}
