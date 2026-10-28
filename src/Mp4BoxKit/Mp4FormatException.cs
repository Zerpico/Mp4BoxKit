namespace Mp4BoxKit;

/// <summary>Thrown when an input is not a supported ISO-BMFF MP4 file.</summary>
public sealed class Mp4FormatException(string message) : Exception(message);
