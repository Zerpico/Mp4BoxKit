namespace Mp4BoxKit.Codecs;

/// <summary>Container box types for opaque codec configuration payloads.</summary>
internal static class CodecConfigurationBoxes
{
    public static FourCc ForCodec(Codec codec) => codec switch
    {
        Codec.H264 => BoxTypes.AvcC,
        Codec.H265 => BoxTypes.HvcC,
        Codec.Vvc => new FourCc("vvcC"),
        Codec.Av1 => new FourCc("av1C"),
        Codec.Vp8 or Codec.Vp9 => new FourCc("vpcC"),
        Codec.Opus => BoxTypes.Dops,
        Codec.Ac3 => new FourCc("dac3"),
        Codec.EAc3 => new FourCc("dec3"),
        Codec.Dts => new FourCc("ddts"),
        Codec.Vc1 => new FourCc("dvc1"),
        Codec.Aac or Codec.Mp3 or Codec.Mpeg1Audio or
            Codec.Mpeg4Video or Codec.Mpeg2Video => BoxTypes.Esds,
        _ => throw new NotSupportedException(
            $"Codec configuration serialization is not implemented for {codec}.")
    };

    public static bool IsConfiguration(FourCc type) =>
        type == BoxTypes.AvcC || type == BoxTypes.HvcC ||
        type == BoxTypes.Dops || type == BoxTypes.Esds ||
        type == new FourCc("vvcC") || type == new FourCc("av1C") ||
        type == new FourCc("vpcC") || type == new FourCc("dac3") ||
        type == new FourCc("dec3") || type == new FourCc("ddts") ||
        type == new FourCc("dvc1");
}
