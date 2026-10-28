namespace Mp4BoxKit.Writer;

internal sealed class FileSample(
    byte[] data, long pts, long dts, SampleFlags flags, long offset)
{
    public readonly byte[] Data = data;
    public int Size = data.Length;
    public readonly long Pts = pts;
    public readonly long Dts = dts;
    public readonly SampleFlags Flags = flags;
    public long Offset = offset;
    public long Duration;
    public bool HasExplicitDuration;
}
