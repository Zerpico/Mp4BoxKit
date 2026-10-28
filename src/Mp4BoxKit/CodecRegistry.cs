namespace Mp4BoxKit;

/// <summary>
/// Maps ISO-BMFF sample-entry types to the codec abstraction used by the API.
/// Codec-specific decoder configuration parsing remains in the corresponding
/// sample-entry parser; this registry only owns FourCC identity.
/// </summary>
internal static class CodecRegistry
{
    public static Codec FromSampleEntry(FourCc type) => type.ToString() switch
    {
        "avc1" or "avc2" or "avc3" or "avc4" => Codec.H264,
        "hvc1" or "hev1" or "dvh1" or "dvhe" => Codec.H265,
        "vvc1" or "vvi1" => Codec.Vvc,
        "av01" => Codec.Av1,
        "vp09" => Codec.Vp9,
        "vp08" => Codec.Vp8,
        "mp4v" => Codec.Mpeg4Video,
        "mp2v" => Codec.Mpeg2Video,
        "jpeg" or "mjpg" => Codec.Jpeg,
        "png " => Codec.Png,
        "mjp2" => Codec.Jpeg2000,
        "mp4a" => Codec.Aac,
        "Opus" => Codec.Opus,
        "ac-3" => Codec.Ac3,
        "ec-3" => Codec.EAc3,
        "dtsc" or "dtsh" or "dtsl" or "dtse" => Codec.Dts,
        "alaw" => Codec.G711Alaw,
        "ulaw" => Codec.G711MuLaw,
        "ipcm" or "fpcm" or "lpcm" or "sowt" or "twos" or "in24" or "in32" => Codec.Pcm,
        "tx3g" or "text" or "c608" => Codec.Tx3g,
        "wvtt" => Codec.WebVtt,
        "stpp" => Codec.Ttml,
        "vc-1" => Codec.Vc1,
        "drac" => Codec.Dirac,
        _ => Codec.Unknown
    };

    public static FourCc GetSampleEntryType(MediaTrackInfo track)
    {
        var type = track.Codec switch
        {
            Codec.H264 => BoxTypes.Avc1,
            Codec.H265 => BoxTypes.Hvc1,
            Codec.Vvc => BoxTypes.Vvc1,
            Codec.Av1 => BoxTypes.Av01,
            Codec.Vp9 => BoxTypes.Vp09,
            Codec.Vp8 => BoxTypes.Vp08,
            Codec.Mpeg4Video => BoxTypes.Mp4v,
            Codec.Mpeg2Video => BoxTypes.Mp2v,
            Codec.Jpeg => BoxTypes.Jpeg,
            Codec.Png => BoxTypes.Png,
            Codec.Jpeg2000 => BoxTypes.Mjp2,
            Codec.Opus => BoxTypes.Opus,
            Codec.Ac3 => BoxTypes.Ac3,
            Codec.EAc3 => BoxTypes.Ec3,
            Codec.Dts => BoxTypes.Dtsc,
            Codec.G711Alaw => BoxTypes.Alaw,
            Codec.G711MuLaw => BoxTypes.Ulaw,
            Codec.Pcm => track is AudioTrackInfo { PcmLittleEndian16: true } ? new FourCc("sowt") : BoxTypes.Ipcm,
            Codec.Tx3g => BoxTypes.Tx3g,
            Codec.WebVtt => BoxTypes.Wvtt,
            Codec.Ttml => BoxTypes.Stpp,
            Codec.Mp3 or Codec.Mpeg1Audio => BoxTypes.Mp4a,
            Codec.Aac => BoxTypes.Mp4a,
            _ => default
        };

        if (type.Value != 0)
        {
            return type;
        }

        throw new NotSupportedException(
            $"Codec {track.Codec} cannot be written as an ISO-BMFF sample entry.");
    }

    public static bool IsVisual(Codec codec) => codec is
        Codec.H264 or Codec.H265 or Codec.Vvc or Codec.Av1 or Codec.Vp8 or
        Codec.Vp9 or Codec.Mpeg4Video or Codec.Mpeg2Video or Codec.Jpeg or
        Codec.Png or Codec.Jpeg2000 or Codec.Vc1 or Codec.Dirac;

    private static readonly HashSet<Codec> WritableVideoCodecs = new()
    {
        Codec.H264, Codec.H265, Codec.Vvc, Codec.Av1, Codec.Vp8, Codec.Vp9,
        Codec.Mpeg4Video, Codec.Mpeg2Video, Codec.Jpeg, Codec.Png, Codec.Jpeg2000
    };

    private static readonly HashSet<Codec> WritableAudioCodecs = new()
    {
        Codec.Aac, Codec.Mp3, Codec.Mpeg1Audio, Codec.Opus, Codec.Ac3,
        Codec.EAc3, Codec.Pcm, Codec.G711Alaw, Codec.G711MuLaw, Codec.Dts
    };

    private static readonly HashSet<Codec> WritableSubtitleCodecs = new()
    {
        Codec.Tx3g
    };

    private static readonly HashSet<Codec> AudioCodecs = new()
    {
        Codec.Aac, Codec.Mp3, Codec.Mpeg1Audio, Codec.Opus, Codec.Ac3,
        Codec.EAc3, Codec.Pcm, Codec.G711Alaw, Codec.G711MuLaw, Codec.Dts
    };

    private static readonly HashSet<Codec> SubtitleCodecs = new()
    {
        Codec.Tx3g, Codec.WebVtt, Codec.Ttml
    };

    private static readonly HashSet<Codec> ConfigurationCodecs = new()
    {
        Codec.H264, Codec.H265, Codec.Vvc, Codec.Av1, Codec.Vp8, Codec.Vp9,
        Codec.Mpeg4Video, Codec.Mpeg2Video, Codec.Aac, Codec.Mp3,
        Codec.Mpeg1Audio, Codec.Opus, Codec.Ac3, Codec.EAc3, Codec.Dts
    };

    public static bool IsWritableVideo(Codec codec) => WritableVideoCodecs.Contains(codec);
    public static bool IsWritableAudio(Codec codec) => WritableAudioCodecs.Contains(codec);
    public static bool IsWritableSubtitle(Codec codec) => WritableSubtitleCodecs.Contains(codec);
    public static bool IsAudio(Codec codec) => AudioCodecs.Contains(codec);
    public static bool IsSubtitle(Codec codec) => SubtitleCodecs.Contains(codec);
    public static bool RequiresConfiguration(Codec codec) => ConfigurationCodecs.Contains(codec);

    public static Codec ResolveMp4aCodec(ReadOnlySpan<byte> esds)
    {
        return Reader.Metadata.ElementaryStreamDescriptorParser.ResolveAudioCodec(esds);
    }
}
