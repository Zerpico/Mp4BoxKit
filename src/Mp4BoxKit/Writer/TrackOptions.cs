namespace Mp4BoxKit;

public sealed record VideoTrackOptions(
    Codec Codec,
    int Width,
    int Height,
    uint TimeScale = 90_000,
    ReadOnlyMemory<byte> CodecConfiguration = default,
    TrackTimeline? Timeline = null);

public sealed record AudioTrackOptions(
    Codec Codec,
    int Channels,
    int SampleRate,
    int BitsPerSample = 16,
    uint TimeScale = 48_000,
    ReadOnlyMemory<byte> CodecConfiguration = default,
    TrackTimeline? Timeline = null,
    bool PcmLittleEndian16 = false);

public sealed record SubtitleTrackOptions(
    Codec Codec,
    uint TimeScale = 1_000,
    ReadOnlyMemory<byte> CodecConfiguration = default,
    int Width = 0,
    int Height = 0,
    string? Name = null,
    bool IsDefault = true,
    string Language = Mp4Languages.Undetermined);
