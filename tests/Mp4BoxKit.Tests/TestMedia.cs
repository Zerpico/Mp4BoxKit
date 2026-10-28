namespace Mp4BoxKit.Tests;

internal static class TestMedia
{
    // Structurally valid avcC: version 1, 4-byte NAL lengths, one SPS and one PPS.
    public static ReadOnlyMemory<byte> AvcC => new byte[]
    {
        0x01, 0x42, 0x00, 0x1E,
        0xFF, 0xE1,
        0x00, 0x01, 0x67,
        0x01,
        0x00, 0x01, 0x68
    };

    // Structurally valid hvcC: version 1, 4-byte NAL lengths, VPS/SPS/PPS arrays.
    public static ReadOnlyMemory<byte> HvcC => new byte[]
    {
        0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x78, 0xF0, 0x00, 0xFC, 0xFD, 0xF8, 0xF8, 0x00, 0x00, 0x0F, 0x03,
        0xA0, 0x00, 0x01, 0x00, 0x01, 0x40,
        0xA1, 0x00, 0x01, 0x00, 0x01, 0x42,
        0xA2, 0x00, 0x01, 0x00, 0x01, 0x44
    };

    public static ReadOnlyMemory<byte> Esds => new byte[] { 0x00, 0x00, 0x00, 0x00 };

    public static ReadOnlyMemory<byte> RawSps => new byte[] { 0x67, 0x42, 0x00, 0x1E };
    public static ReadOnlyMemory<byte> RawPps => new byte[] { 0x68 };

    public static byte[] VideoSample(bool keyFrame = true)
        => VideoSample(0x88, keyFrame);

    public static byte[] VideoSample(byte marker, bool keyFrame = true)
        => keyFrame
            ? new byte[] { 0x00, 0x00, 0x00, 0x02, 0x65, marker }
            : new byte[] { 0x00, 0x00, 0x00, 0x02, 0x41, marker };

    public static byte[] H265VideoSample(byte marker = 0x88, bool keyFrame = true)
        => keyFrame
            ? new byte[] { 0x00, 0x00, 0x00, 0x02, 0x26, marker } // IDR_W_RADL
            : new byte[] { 0x00, 0x00, 0x00, 0x02, 0x02, marker }; // TRAIL_R

    public static ReadOnlyMemory<byte> Configuration(Codec codec) => codec switch
    {
        Codec.H264 => AvcC,
        Codec.H265 => HvcC,
        Codec.Vvc or Codec.Av1 or Codec.Vp8 or Codec.Vp9
            or Codec.Mpeg4Video or Codec.Mpeg2Video => Esds,
        Codec.Aac or Codec.Mp3 or Codec.Mpeg1Audio or Codec.Opus
            or Codec.Ac3 or Codec.EAc3 or Codec.Dts => Esds,
        _ => default
    };
}
