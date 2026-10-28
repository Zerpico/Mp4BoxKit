namespace Mp4BoxKit.Boxes;

internal static class IsoBmffConstants
{
    public const uint FileTypeMinorVersion = 0x200;
    public const uint FullBoxVersionOne = 0x01000000;
    public const uint TrackEnabledAndInMovie = 3;
    public const ushort AudioTrackVolume = 0x0100;
    public const ushort UndeterminedLanguage = 0x55c4;
    public const uint FixedPointOne = 0x00010000;
    public const uint FixedPointOneHalf = 0x00008000;
    public const uint IdentityMatrixLast = 0x40000000;
    public const uint VideoResolution = 0x00480000;
    public const ushort VideoFrameCount = 1;
    public const ushort VideoDepth = 0x0018;
    public const ushort VideoColorTable = 0xffff;
    public const uint TfhdDefaultBaseIsMoof = 0x020000;
    public const uint TrunDataOffsetPresent = 0x00000001;
    public const uint TrunSampleDurationPresent = 0x00000100;
    public const uint TrunSampleSizePresent = 0x00000200;
    public const uint TrunSampleFlagsPresent = 0x00000400;
    public const uint TrunSampleCompositionTimeOffsetPresent = 0x00000800;
    public const uint TrunFields =
        TrunDataOffsetPresent |
        TrunSampleDurationPresent |
        TrunSampleSizePresent |
        TrunSampleFlagsPresent |
        TrunSampleCompositionTimeOffsetPresent;
    public const uint NonSyncSample = 0x00010000;
}
