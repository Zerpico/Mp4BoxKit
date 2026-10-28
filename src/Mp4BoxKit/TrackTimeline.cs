namespace Mp4BoxKit;

/// <summary>Maps source timestamps to nonnegative MP4 media time. Values are in track ticks.</summary>
/// <param name="DecodeOrigin">Source decode time subtracted from input PTS and DTS.</param>
/// <param name="PresentationStart">Source time mapped to the beginning of the media edit.</param>
/// <param name="PresentationDelay">Nonnegative delay before the media edit, expressed in track ticks.
/// Serialized in movie ticks (milliseconds), rounded down.</param>
public sealed record TrackTimeline(long DecodeOrigin, long PresentationStart = 0,
    long PresentationDelay = 0);
