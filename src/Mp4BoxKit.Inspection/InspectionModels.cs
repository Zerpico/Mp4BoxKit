namespace Mp4BoxKit.Inspection;

/// <summary>A named value extracted from a box payload.</summary>
public sealed class BoxField
{
    public string Name { get; init; } = "";
    public string Value { get; init; } = "";
}

/// <summary>One node in the ISO-BMFF box tree.</summary>
public sealed class BoxNode
{
    public string Type { get; init; } = "";
    public long Offset { get; init; }
    public long Size { get; init; }
    public int HeaderSize { get; init; }
    public int Depth { get; init; }
    public bool IsFullBox { get; set; }
    public uint Version { get; set; }
    public uint Flags { get; set; }
    public bool Truncated { get; init; }
    public List<BoxField> Fields { get; } = new();
    public List<BoxNode> Children { get; } = new();
    public long PayloadSize => Size - HeaderSize;
}

/// <summary>Movie-level values read from ftyp/mvhd/mvex.</summary>
public sealed class MovieSummary
{
    public string MajorBrand { get; init; } = "";
    public uint MinorVersion { get; init; }
    public bool IsFragmented { get; init; }
    public uint Timescale { get; init; }
    public ulong Duration { get; init; }
    public double DurationSeconds { get; init; }
    public int TrackCount { get; init; }
}

/// <summary>Track metadata exposed by the inspection layer.</summary>
public sealed class TrackSummary
{
    public uint Id { get; init; }
    public string Type { get; init; } = "";
    public string Codec { get; init; } = "";
    public uint TimeScale { get; init; }
    public long Duration { get; init; }
    public double DurationSeconds { get; init; }
    public string? Language { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int Channels { get; init; }
    public int SampleRate { get; init; }
}

/// <summary>Metadata for one decoded sample.</summary>
public sealed class SampleRow
{
    public uint Track { get; init; }
    public long Pts { get; init; }
    public long Dts { get; init; }
    public int Duration { get; init; }
    public int Size { get; init; }
    public bool KeyFrame { get; init; }
}

/// <summary>One moof+mdat pair in a fragmented MP4 file.</summary>
public sealed class SegmentInfo
{
    public uint SequenceNumber { get; init; }
    public long Offset { get; init; }
    public long Size { get; init; }
    public long MoofOffset { get; init; }
    public long MdatOffset { get; init; }
    public long MdatSize { get; init; }
    public int SampleCount { get; init; }
    public long EarliestPresentationTime { get; init; }
    public long Duration { get; init; }
    public List<uint> Tracks { get; } = new();
}

/// <summary>Complete result of an inspection run.</summary>
public sealed class Mp4Inspection
{
    public string FileName { get; init; } = "";
    public long Size { get; init; }
    public MovieSummary Movie { get; init; } = new();
    public List<TrackSummary> Tracks { get; } = new();
    public List<BoxNode> Boxes { get; } = new();
    public List<SampleRow> Samples { get; } = new();
    public List<SegmentInfo> Segments { get; } = new();
    public List<string> Warnings { get; } = new();
}
