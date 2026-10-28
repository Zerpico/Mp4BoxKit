using Mp4BoxKit.Inspection;

namespace Mp4BoxKit.Cli;

internal sealed class CliOptions
{
    public string? InputPath { get; set; }
    public bool ShowHelp { get; set; }
    public bool ShowVersion { get; set; }
    public bool Summary { get; set; }
    public bool Boxes { get; set; }
    public bool Samples { get; set; }
    public bool Segments { get; set; }
    public int MaxDepth { get; set; } = 12;
    public int MaxBoxes { get; set; } = 10_000;
    public int MaxSamples { get; set; } = 1_000;
    public int MaxSegments { get; set; } = 1_000;
    public long MaxInputBytes { get; set; } = 512L * 1024 * 1024;
    public string? JsonPath { get; set; }
    public string? CsvDirectory { get; set; }
    public string? BoxesCsvPath { get; set; }
    public string? SamplesCsvPath { get; set; }
    public string? SegmentsCsvPath { get; set; }

    public InspectOptions ToInspectOptions()
        => new()
        {
            MaxDepth = MaxDepth,
            MaxBoxes = MaxBoxes,
            IncludeBoxes = Boxes
                || JsonPath is not null
                || BoxesCsvPath is not null,
            IncludeSamples = Samples || SamplesCsvPath is not null,
            MaxSamples = MaxSamples,
            IncludeSegments = Segments
                || JsonPath is not null
                || SegmentsCsvPath is not null,
            MaxSegments = MaxSegments,
            MaxInputBytes = MaxInputBytes
        };
}
