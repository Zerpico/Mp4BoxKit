namespace Mp4BoxKit;

[Flags]
public enum SampleFlags
{
    None = 0,
    KeyFrame = 1,
    SegmentForce = 0x8000,
    SegmentDisable = 0x4000
}

public enum MediaType
{
    Video,
    Audio,
    Subtitle
}

public enum Codec
{
    Unknown,
    Mpeg2Video,
    Mpeg4Video,
    H261,
    H263,
    H264,
    H265,
    Vp8,
    Vp9,
    Av1,
    Aac,
    Mp3,
    Opus,
    Ac3,
    EAc3,
    Pcm,
    G711Alaw,
    G711MuLaw,
    Tx3g,
    WebVtt,
    Ttml,
    Mpeg1Audio,
    Jpeg,
    Png,
    Jpeg2000,
    Vvc,
    Dts,
    Vc1,
    Dirac
}

public enum ProbeStatus
{
    Complete,
    NeedMoreData,
    EndOfStream,
    InvalidFormat
}

public enum UnknownBoxHandling
{
    Skip,
    Report
}

public enum Fmp4SegmentKind
{
    Initialization,
    Media,
    /// <summary>Trailing whole-file random-access index (mfra). Append after all media parts.</summary>
    Index
}

public enum Fmp4FragmentBoundary
{
    EveryKeyFrame,
    KeyFrameAfterTargetDuration,
    Manual
}

public readonly record struct TrackId(uint Value);

public readonly record struct FourCc(uint Value)
{
    public FourCc(string value)
        : this(FromString(value))
    {
    }

    public static FourCc FromString(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        if (value.Length != 4)
        {
            throw new ArgumentException(
                "FourCC must contain four characters.", nameof(value));
        }

        return new FourCc(
            (uint)value[0] << 24 |
            (uint)value[1] << 16 |
            (uint)value[2] << 8 |
            value[3]);
    }

    public override string ToString()
    {
        return string.Create(4, Value, static (span, value) =>
        {
            span[0] = (char)((value >> 24) & 0xff);
            span[1] = (char)((value >> 16) & 0xff);
            span[2] = (char)((value >> 8) & 0xff);
            span[3] = (char)(value & 0xff);
        });
    }

    public static implicit operator uint(FourCc value) => value.Value;
}

public static class BoxTypes
{
    public static readonly FourCc Ftyp = new("ftyp");
    public static readonly FourCc Moov = new("moov");
    public static readonly FourCc Mdat = new("mdat");
    public static readonly FourCc Moof = new("moof");
    public static readonly FourCc Mfra = new("mfra");

    public static readonly FourCc Free = new("free");
    public static readonly FourCc Styp = new("styp");
    public static readonly FourCc Sidx = new("sidx");
    public static readonly FourCc Trak = new("trak");
    public static readonly FourCc Mvhd = new("mvhd");

    public static readonly FourCc Tkhd = new("tkhd");
    public static readonly FourCc Mdia = new("mdia");
    public static readonly FourCc Mdhd = new("mdhd");
    public static readonly FourCc Hdlr = new("hdlr");
    public static readonly FourCc Minf = new("minf");

    public static readonly FourCc Vmhd = new("vmhd");
    public static readonly FourCc Smhd = new("smhd");
    public static readonly FourCc Nmhd = new("nmhd");

    public static readonly FourCc Stbl = new("stbl");
    public static readonly FourCc Stsd = new("stsd");
    public static readonly FourCc Stts = new("stts");
    public static readonly FourCc Ctts = new("ctts");
    public static readonly FourCc Stsc = new("stsc");

    public static readonly FourCc Dinf = new("dinf");
    public static readonly FourCc Dref = new("dref");
    public static readonly FourCc Url = new("url ");

    public static readonly FourCc Stsz = new("stsz");
    public static readonly FourCc Stco = new("stco");
    public static readonly FourCc Co64 = new("co64");
    public static readonly FourCc Stss = new("stss");
    public static readonly FourCc Mvex = new("mvex");

    public static readonly FourCc Trex = new("trex");
    public static readonly FourCc Mfhd = new("mfhd");
    public static readonly FourCc Traf = new("traf");
    public static readonly FourCc Tfhd = new("tfhd");
    public static readonly FourCc Tfdt = new("tfdt");

    public static readonly FourCc Trun = new("trun");
    public static readonly FourCc Tfra = new("tfra");
    public static readonly FourCc Mfro = new("mfro");
    public static readonly FourCc Avc1 = new("avc1");
    public static readonly FourCc Hvc1 = new("hvc1");

    public static readonly FourCc Mp4a = new("mp4a");
    public static readonly FourCc AvcC = new("avcC");
    public static readonly FourCc HvcC = new("hvcC");
    public static readonly FourCc Esds = new("esds");
    public static readonly FourCc Av01 = new("av01");
    public static readonly FourCc Mp2v = new("mp2v");

    public static readonly FourCc Vp09 = new("vp09");
    public static readonly FourCc Vp08 = new("vp08");
    public static readonly FourCc Opus = new("Opus");
    public static readonly FourCc Dops = new("dOps");
    public static readonly FourCc Tx3g = new("tx3g");

    public static readonly FourCc Wvtt = new("wvtt");
    public static readonly FourCc Stpp = new("stpp");
    public static readonly FourCc Vvc1 = new("vvc1");
    public static readonly FourCc Mp4v = new("mp4v");

    public static readonly FourCc Jpeg = new("jpeg");
    public static readonly FourCc Png = new("png ");
    public static readonly FourCc Mjp2 = new("mjp2");

    public static readonly FourCc Ac3 = new("ac-3");
    public static readonly FourCc Ec3 = new("ec-3");
    public static readonly FourCc Dtsc = new("dtsc");

    public static readonly FourCc Alaw = new("alaw");
    public static readonly FourCc Ulaw = new("ulaw");
    public static readonly FourCc Ipcm = new("ipcm");

    public static readonly FourCc DvH1 = new("dvh1");
    public static readonly FourCc DvHe = new("dvhe");
    public static readonly FourCc Vc1 = new("vc-1");
    public static readonly FourCc Drac = new("drac");
}

public abstract class MediaTrackInfo
{
    public required TrackId Id { get; init; }
    public required Codec Codec { get; init; }
    public uint TimeScale { get; init; }
    public TrackTimeline? Timeline { get; init; }
    /// <summary>Media time mapped to presentation time zero by a simple edit list, in track ticks.</summary>
    public long PresentationMediaStart { get; init; }
    public ReadOnlyMemory<byte> CodecConfiguration { get; init; }
    public abstract MediaType Type { get; }
}

public sealed class VideoTrackInfo : MediaTrackInfo
{
    public override MediaType Type => MediaType.Video;
    public int Width { get; init; }
    public int Height { get; init; }
}

public sealed class AudioTrackInfo : MediaTrackInfo
{
    public bool PcmLittleEndian16 { get; init; }
    public override MediaType Type => MediaType.Audio;
    public int Channels { get; init; }
    public int SampleRate { get; init; }
    public int BitsPerSample { get; init; } = 16;
}

public sealed class SubtitleTrackInfo : MediaTrackInfo
{
    public string? Name { get; init; }
    public bool IsDefault { get; init; } = true;
    public string Language { get; init; } = Mp4Languages.Undetermined;
    public int Width { get; init; }
    public int Height { get; init; }
    public override MediaType Type => MediaType.Subtitle;
}

public sealed class ContainerInfo
{
    public bool IsFragmented { get; internal set; }
    public FourCc MajorBrand { get; internal set; }
    public List<MediaTrackInfo> Tracks { get; } = new();
}

public readonly record struct SampleInfo(
    TrackId Track,
    long Pts,
    long Dts,
    int Duration,
    SampleFlags Flags);

public delegate void SampleHandler(in SampleInfo info, ReadOnlySpan<byte> data);

/// <summary>
/// A sample returned by the unified reader API.
/// The data is owned by this result and remains valid after the read operation.
/// </summary>
public sealed class Mp4Sample
{
    private readonly byte[] _data;

    public SampleInfo Info { get; }
    public ReadOnlyMemory<byte> Data => _data;

    public TrackId Track => Info.Track;
    public long Pts => Info.Pts;
    public long Dts => Info.Dts;
    public int Duration => Info.Duration;
    public SampleFlags Flags => Info.Flags;

    internal Mp4Sample(SampleInfo info, ReadOnlySpan<byte> data)
    {
        Info = info;
        _data = data.ToArray();
    }
}

/// <summary>
/// One independently deliverable part of an fMP4 stream: initialization,
/// media segment, or trailing index.
/// </summary>
public sealed class Fmp4Segment
{
    public Fmp4SegmentKind Kind { get; }
    public uint SequenceNumber { get; }
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Byte offset of this part in the concatenated stream.</summary>
    public long Offset { get; }

    /// <summary>Track whose timing describes the media segment.</summary>
    public TrackId ReferenceTrack { get; }

    /// <summary>Timescale of <see cref="ReferenceTrack"/>.</summary>
    public uint TimeScale { get; }

    /// <summary>Earliest presentation time in <see cref="TimeScale"/> units.</summary>
    public long EarliestPresentationTime { get; }

    /// <summary>Presentation duration in <see cref="TimeScale"/> units.</summary>
    public long Duration { get; }

    /// <summary>True when the reference track starts with a sync sample.</summary>
    public bool StartsWithKeyFrame { get; }

    /// <summary>Number of samples across all tracks in this fragment.</summary>
    public int SampleCount { get; }

    internal Fmp4Segment(
        Fmp4SegmentKind kind,
        uint sequenceNumber,
        byte[] data,
        long offset = 0,
        TrackId referenceTrack = default,
        uint timeScale = 0,
        long earliestPresentationTime = 0,
        long duration = 0,
        bool startsWithKeyFrame = false,
        int sampleCount = 0)
    {
        Kind = kind;
        SequenceNumber = sequenceNumber;
        Data = data;
        Offset = offset;
        ReferenceTrack = referenceTrack;
        TimeScale = timeScale;
        EarliestPresentationTime = earliestPresentationTime;
        Duration = duration;
        StartsWithKeyFrame = startsWithKeyFrame;
        SampleCount = sampleCount;
    }
}
