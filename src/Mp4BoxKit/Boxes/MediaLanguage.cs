namespace Mp4BoxKit.Boxes;

internal static class MediaLanguage
{
    public static ushort Pack(string language)
    {
        ArgumentNullException.ThrowIfNull(language);
        if (language.Length != 3 || language.Any(c => c is < 'a' or > 'z'))
            throw new ArgumentException("Use a three-letter lowercase ISO 639-2/T code.", nameof(language));
        return (ushort)(((language[0] - 0x60) << 10) |
            ((language[1] - 0x60) << 5) | (language[2] - 0x60));
    }

    public static string Unpack(ushort value) => new(new[]
    {
        (char)(((value >> 10) & 31) + 0x60),
        (char)(((value >> 5) & 31) + 0x60),
        (char)((value & 31) + 0x60)
    });
}
