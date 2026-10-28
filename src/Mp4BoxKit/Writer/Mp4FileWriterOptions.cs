namespace Mp4BoxKit;

/// <summary>Options for classic MP4 output.</summary>
public sealed class Mp4FileWriterOptions
{
    /// <summary>Write ftyp/moov before mdat using a temporary spool.</summary>
    public bool FastStart { get; init; }
}
