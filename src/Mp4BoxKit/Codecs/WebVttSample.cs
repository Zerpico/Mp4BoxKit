using System.Buffers.Binary;
using System.Text;

namespace Mp4BoxKit.Codecs;

/// <summary>One WebVTT cue without timing; timing comes from the MP4 sample.</summary>
/// <param name="Payload">Cue text, may contain line breaks and WebVTT tags such as <c>&lt;b&gt;</c>.</param>
/// <param name="Id">Optional cue identifier (iden box).</param>
/// <param name="Settings">Optional cue settings, for example <c>"line:90% align:start"</c> (sttg box).</param>
public sealed record WebVttCue(string Payload, string? Id = null, string? Settings = null);

/// <summary>
/// Serializes ISO/IEC 14496-30 WebVTT samples. A sample contains one vttc box per
/// cue visible during the whole sample, or a single vtte box for an empty interval.
/// Cue timestamps are not stored in the payload: pass them as sample DTS/duration.
/// </summary>
public static class WebVttSample
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly byte[] EmptySample = [0, 0, 0, 8, (byte)'v', (byte)'t', (byte)'t', (byte)'e'];

    /// <summary>A sample with no visible cues (vtte box).</summary>
    public static ReadOnlyMemory<byte> Empty => EmptySample;

    /// <summary>Builds a sample with all cues visible during it; no cues yields <see cref="Empty"/>.</summary>
    public static ReadOnlyMemory<byte> Create(params IEnumerable<WebVttCue> cues)
    {
        ArgumentNullException.ThrowIfNull(cues);
        using var output = new MemoryStream();
        foreach (var cue in cues)
        {
            ArgumentNullException.ThrowIfNull(cue);
            ArgumentNullException.ThrowIfNull(cue.Payload);
            var id = Text(cue.Id, nameof(cue.Id));
            var settings = Text(cue.Settings, nameof(cue.Settings));
            var payload = Text(cue.Payload, nameof(cue.Payload))!;
            var size = checked(8 + 8 + payload.Length);
            if (id is { Length: > 0 })
                size = checked(size + 8 + id.Length);
            if (settings is { Length: > 0 })
                size = checked(size + 8 + settings.Length);

            WriteHeader(output, size, BoxTypes.VttCue);
            if (id is { Length: > 0 })
                WriteBox(output, BoxTypes.VttCueId, id);
            if (settings is { Length: > 0 })
                WriteBox(output, BoxTypes.VttCueSettings, settings);
            WriteBox(output, BoxTypes.VttCuePayload, payload);
        }

        return output.Length == 0 ? EmptySample : output.ToArray();
    }

    private static byte[]? Text(string? value, string name)
    {
        if (value is null)
            return null;
        if (value.Contains("-->", StringComparison.Ordinal))
            throw new ArgumentException($"WebVTT cue {name} must not contain \"-->\".", name);
        return StrictUtf8.GetBytes(value);
    }

    private static void WriteBox(MemoryStream output, FourCc type, byte[] payload)
    {
        WriteHeader(output, checked(8 + payload.Length), type);
        output.Write(payload);
    }

    private static void WriteHeader(MemoryStream output, int size, FourCc type)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)size));
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], type.Value);
        output.Write(header);
    }

    internal static void Validate(ReadOnlySpan<byte> sample)
    {
        if (sample.IsEmpty)
            throw new ArgumentException(
                "WebVTT sample is empty; use WebVttSample.Empty for intervals without cues.");

        var offset = 0;
        var cues = 0;
        var empty = 0;
        while (offset < sample.Length)
        {
            if (sample.Length - offset < 8)
                throw InvalidSample();
            var size = BinaryPrimitives.ReadUInt32BigEndian(sample[offset..]);
            var type = new FourCc(BinaryPrimitives.ReadUInt32BigEndian(sample[(offset + 4)..]));
            if (size < 8 || size > sample.Length - offset)
                throw InvalidSample();
            if (type == BoxTypes.VttCue)
                cues++;
            else if (type == BoxTypes.VttEmptyCue)
                empty++;
            else if (type != BoxTypes.VttAdditionalText)
                throw InvalidSample();
            offset += (int)size;
        }

        if (cues + empty == 0)
            throw new ArgumentException("A WebVTT sample must contain a vttc or vtte box.");
        if (empty > 1 || (empty == 1 && cues != 0))
            throw new ArgumentException("A vtte box must be the only cue box in a WebVTT sample.");
    }

    private static ArgumentException InvalidSample() => new(
        "WebVTT sample must be a sequence of vttc/vtte/vtta boxes; " +
        "build it with WebVttSample.Create or WebVttSampleBuilder.");
}
