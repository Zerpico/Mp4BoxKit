namespace Mp4BoxKit;

/// <summary>Options for fragmented MP4 output and segment delivery.</summary>
public sealed class Mp4FragmentedWriterOptions
{
    /// <summary>
    /// Prefix each media segment with a single-reference sidx.
    /// Does not retain segment history.
    /// </summary>
    public bool WriteSegmentIndex { get; init; }

    /// <summary>
    /// Append mfra/tfra on Complete to output and, if supplied, the callback as
    /// an Index part. Offsets refer to the concatenation of initialization,
    /// media and index parts starting at byte zero. Retains metadata for the
    /// entire recording; intended for finite archives, not bounded-memory live
    /// delivery.
    /// </summary>
    public bool WriteRandomAccessIndex { get; init; }

    /// <summary>
    /// Maximum buffered fragment payload. Exceeding the limit rejects the
    /// sample before copying it.
    /// </summary>
    public long MaxFragmentBytes { get; init; } = 64 * 1024 * 1024;

    /// <summary>Maximum buffered samples across all tracks in one fragment.</summary>
    public int MaxFragmentSamples { get; init; } = 100_000;

    /// <summary>
    /// Target fragment duration. When unset, video keyframes drive boundaries
    /// and non-video streams use a 2-second fallback.
    /// </summary>
    public TimeSpan? TargetFragmentDuration { get; init; }

    /// <summary>
    /// Prefix media segments with an ISO-BMFF <c>styp</c> box.
    /// Recommended for HLS/DASH; enabled by default.
    /// </summary>
    public bool WriteSegmentType { get; init; } = true;

    public Fmp4FragmentBoundary FragmentBoundary { get; init; }
        = Fmp4FragmentBoundary.EveryKeyFrame;
}
