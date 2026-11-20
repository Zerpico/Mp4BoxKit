using System.Text;

namespace Mp4BoxKit.Codecs;

/// <summary>
/// Builds the vttC payload of a WebVTT (wvtt) sample entry: the WebVTT file
/// header up to, but not including, the first cue (the signature line plus
/// optional STYLE/REGION blocks).
/// </summary>
public static class WebVttConfiguration
{
    public const string DefaultHeader = "WEBVTT";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <param name="header">For example <c>"WEBVTT"</c> or <c>"WEBVTT\n\nSTYLE\n::cue { color: yellow }"</c>.</param>
    public static ReadOnlyMemory<byte> Create(string header = DefaultHeader)
    {
        ArgumentNullException.ThrowIfNull(header);
        var bytes = StrictUtf8.GetBytes(header);
        Validate(bytes);
        return bytes;
    }

    internal static void Validate(ReadOnlySpan<byte> configuration)
    {
        string text;
        try
        {
            text = StrictUtf8.GetString(configuration);
        }
        catch (DecoderFallbackException error)
        {
            throw new ArgumentException("WebVTT configuration must be valid UTF-8.", error);
        }

        if (!text.StartsWith(DefaultHeader, StringComparison.Ordinal) ||
            (text.Length > DefaultHeader.Length &&
                text[DefaultHeader.Length] is not (' ' or '\t' or '\n' or '\r')))
            throw new ArgumentException("WebVTT configuration must start with the \"WEBVTT\" signature.");
        if (text.Contains("-->", StringComparison.Ordinal))
            throw new ArgumentException("WebVTT configuration must not contain cues; write cues as samples.");
    }
}
