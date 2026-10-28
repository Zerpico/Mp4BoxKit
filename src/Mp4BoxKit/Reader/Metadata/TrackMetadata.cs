namespace Mp4BoxKit.Reader.Metadata;

internal sealed class TrackMetadata
{
    public bool PcmLittleEndian16;
    public int BitsPerSample = 16;
    public long PresentationMediaStart;
    public ulong EmptyEditDuration;
    public string? Name;
    public bool IsDefault = true;
    public string Language = Mp4Languages.Undetermined;
    public uint Id;
    public uint TimeScale = 1_000;
    public MediaType MediaType = MediaType.Video;
    public Codec Codec = Codec.Unknown;
    public int Width;
    public int Height;
    public int Channels;
    public int SampleRate;
    public ReadOnlyMemory<byte> CodecConfiguration;
}
