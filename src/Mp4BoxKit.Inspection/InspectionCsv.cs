using System.Globalization;
using System.Text;

namespace Mp4BoxKit.Inspection;

/// <summary>CSV exports for inspection results.</summary>
public static class InspectionCsv
{
    public static string Boxes(IEnumerable<BoxNode> boxes)
    {
        var builder = new StringBuilder();
        builder.AppendLine("offset,size,headerSize,depth,type,path,version,flags,fields");
        foreach (var box in boxes)
        {
            AppendBox(builder, box, "");
        }

        return builder.ToString();
    }

    public static string Tracks(IEnumerable<TrackSummary> tracks)
    {
        var builder = new StringBuilder();
        builder.AppendLine("id,type,codec,timeScale,duration,durationSeconds,language,width,height,channels,sampleRate");
        foreach (var track in tracks)
        {
            builder.Append(track.Id.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(Escape(track.Type)).Append(',');
            builder.Append(Escape(track.Codec)).Append(',');
            builder.Append(track.TimeScale.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(track.Duration.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(track.DurationSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            builder.Append(Escape(track.Language ?? "")).Append(',');
            builder.Append(track.Width.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(track.Height.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(track.Channels.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(track.SampleRate.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public static string Samples(IEnumerable<SampleRow> samples)
    {
        var builder = new StringBuilder();
        builder.AppendLine("track,pts,dts,duration,size,keyFrame");
        foreach (var sample in samples)
        {
            builder.Append(sample.Track.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(sample.Pts.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(sample.Dts.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(sample.Duration.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(sample.Size.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(sample.KeyFrame ? "true" : "false");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public static string Segments(IEnumerable<SegmentInfo> segments)
    {
        var builder = new StringBuilder();
        builder.AppendLine("sequenceNumber,offset,size,moofOffset,mdatOffset,mdatSize,sampleCount,earliestPresentationTime,duration,tracks");
        foreach (var segment in segments)
        {
            builder.Append(segment.SequenceNumber.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(segment.Offset.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(segment.Size.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(segment.MoofOffset.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(segment.MdatOffset.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(segment.MdatSize.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(segment.SampleCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(segment.EarliestPresentationTime.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(segment.Duration.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(Escape(string.Join(';', segment.Tracks)));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static void AppendBox(
        StringBuilder builder,
        BoxNode box,
        string parentPath)
    {
        var path = parentPath.Length == 0
            ? box.Type
            : $"{parentPath}/{box.Type}";
        var fields = string.Join(';', box.Fields.Select(
            static field => $"{field.Name}={field.Value}"));

        builder.Append(box.Offset.ToString(CultureInfo.InvariantCulture)).Append(',');
        builder.Append(box.Size.ToString(CultureInfo.InvariantCulture)).Append(',');
        builder.Append(box.HeaderSize.ToString(CultureInfo.InvariantCulture)).Append(',');
        builder.Append(box.Depth.ToString(CultureInfo.InvariantCulture)).Append(',');
        builder.Append(Escape(box.Type)).Append(',');
        builder.Append(Escape(path)).Append(',');
        builder.Append(box.Version.ToString(CultureInfo.InvariantCulture)).Append(',');
        builder.Append(box.Flags.ToString(CultureInfo.InvariantCulture)).Append(',');
        builder.Append(Escape(fields));
        builder.AppendLine();

        foreach (var child in box.Children)
        {
            AppendBox(builder, child, path);
        }
    }

    private static string Escape(string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
