namespace Mp4BoxKit.Inspection;

/// <summary>Controls how much of an MP4 file is inspected.</summary>
public sealed class InspectOptions
{
    /// <summary>Maximum box nesting depth. Values are clamped to 1..64.</summary>
    public int MaxDepth { get; init; } = 12;

    /// <summary>Maximum number of boxes in the tree.</summary>
    public int MaxBoxes { get; init; } = 10_000;

    /// <summary>Include the box tree in the result.</summary>
    public bool IncludeBoxes { get; init; } = true;

    /// <summary>Read sample metadata and payload sizes.</summary>
    public bool IncludeSamples { get; init; }

    /// <summary>Maximum samples to read. Use int.MaxValue for all samples.</summary>
    public int MaxSamples { get; init; } = 1_000;

    /// <summary>Include fragmented media segment summaries.</summary>
    public bool IncludeSegments { get; init; } = true;

    /// <summary>Maximum segments to report. Use int.MaxValue for all segments.</summary>
    public int MaxSegments { get; init; } = 1_000;

    /// <summary>Maximum input size accepted by the inspector.</summary>
    public long MaxInputBytes { get; init; } = 512L * 1024 * 1024;
}
