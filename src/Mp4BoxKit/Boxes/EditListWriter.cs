namespace Mp4BoxKit.Boxes;

internal static class EditListWriter
{
    public static void Write(IsoBoxBuffer buffer, MediaTrackInfo track, ulong duration = 0)
    {
        if (track.Timeline is not { } timeline)
        {
            return;
        }
        var start = checked(timeline.PresentationStart - timeline.DecodeOrigin);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        IsoBoxWriter.Box(buffer, new FourCc("edts"), edits =>
            IsoBoxWriter.Box(edits, new FourCc("elst"), list =>
            {
                list.WriteUInt32(0x01000000);
                list.WriteUInt32(timeline.PresentationDelay > 0 ? 2u : 1u);
                if (timeline.PresentationDelay > 0)
                {
                    var delayTicks = checked((ulong)(
                        (UInt128)(ulong)timeline.PresentationDelay *
                        1000 /
                        track.TimeScale));
                    list.WriteUInt64(delayTicks);
                    list.WriteUInt64(ulong.MaxValue); // empty edit
                    list.WriteUInt16(1);
                    list.WriteUInt16(0);
                }
                list.WriteUInt64(duration);
                list.WriteUInt64(checked((ulong)start));
                list.WriteUInt16(1);
                list.WriteUInt16(0);
            }));
    }
}
