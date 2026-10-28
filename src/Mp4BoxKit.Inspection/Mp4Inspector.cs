using System.Globalization;
using System.Runtime.InteropServices;

namespace Mp4BoxKit.Inspection;

/// <summary>Reads ISO-BMFF structure, track metadata, samples, and fMP4 segments.</summary>
public static class Mp4Inspector
{
    /// <summary>Inspects the remaining bytes of a stream.</summary>
    public static async ValueTask<Mp4Inspection> InspectAsync(
        Stream input,
        string? fileName = null,
        InspectOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var resolved = options ?? new InspectOptions();
        ValidateOptions(resolved);
        var data = await ReadInputAsync(input, resolved, cancellationToken)
            .ConfigureAwait(false);
        return Inspect(data, fileName, resolved, cancellationToken);
    }

    /// <summary>Inspects an in-memory MP4 file.</summary>
    public static ValueTask<Mp4Inspection> InspectAsync(
        ReadOnlyMemory<byte> data,
        string? fileName = null,
        InspectOptions? options = null,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Inspect(data, fileName, options, cancellationToken));

    /// <summary>Inspects an unmanaged input buffer without copying it.</summary>
    public static unsafe Mp4Inspection Inspect(
        byte* data,
        int length,
        string? fileName = null,
        InspectOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        var resolved = options ?? new InspectOptions();
        ValidateOptions(resolved);
        if (length > resolved.MaxInputBytes)
        {
            throw new InvalidDataException(
                $"Input is {length} bytes; the configured limit is {resolved.MaxInputBytes} bytes.");
        }

        var span = new ReadOnlySpan<byte>(data, length);
        return InspectCore(
            span,
            () => new UnmanagedMemoryStream(data, length, length, FileAccess.Read),
            fileName,
            resolved,
            cancellationToken);
    }

    /// <summary>Synchronous facade for in-memory input.</summary>
    public static Mp4Inspection Inspect(
        ReadOnlyMemory<byte> data,
        string? fileName = null,
        InspectOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var resolved = options ?? new InspectOptions();
        ValidateOptions(resolved);
        if (data.Length > resolved.MaxInputBytes)
        {
            throw new InvalidDataException(
                $"Input is {data.Length} bytes; the configured limit is {resolved.MaxInputBytes} bytes.");
        }

        return InspectCore(
            data.Span,
            () => CreateStream(data),
            fileName,
            resolved,
            cancellationToken);
    }

    private static Mp4Inspection InspectCore(
        ReadOnlySpan<byte> data,
        Func<Stream> createStream,
        string? fileName,
        InspectOptions options,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var boxes = BoxTreeReader.Read(data, options, warnings);
        var container = Probe(createStream(), options, warnings, cancellationToken);
        var tracks = BuildTracks(container, boxes, warnings);
        var movie = BuildMovieSummary(boxes, tracks, warnings);
        var segments = BuildSegments(boxes, options, warnings);
        var samples = ReadSamples(createStream(), options, warnings, cancellationToken);

        var result = new Mp4Inspection
        {
            FileName = fileName ?? "input.mp4",
            Size = data.Length,
            Movie = movie
        };

        if (options.IncludeBoxes)
        {
            result.Boxes.AddRange(boxes);
        }

        result.Tracks.AddRange(tracks);
        result.Samples.AddRange(samples);
        result.Segments.AddRange(segments);
        result.Warnings.AddRange(warnings);
        return result;
    }

    private static Stream CreateStream(ReadOnlyMemory<byte> data)
    {
        if (MemoryMarshal.TryGetArray(data, out var segment) && segment.Array is { } array)
        {
            return new MemoryStream(array, segment.Offset, segment.Count, writable: false);
        }

        return new MemoryStream(data.ToArray(), writable: false);
    }

    private static async ValueTask<byte[]> ReadInputAsync(
        Stream input,
        InspectOptions options,
        CancellationToken cancellationToken)
    {
        if (input.CanSeek && input.Length - input.Position > options.MaxInputBytes)
        {
            throw new InvalidDataException(
                $"Input is {input.Length - input.Position} bytes; the configured limit is {options.MaxInputBytes} bytes.");
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(chunk, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > options.MaxInputBytes)
            {
                throw new InvalidDataException(
                    $"Input exceeds the configured limit of {options.MaxInputBytes} bytes.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static ContainerInfo? Probe(
        Stream stream,
        InspectOptions options,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            using var probeStream = stream;
            var probe = Mp4Metadata.ProbeAsync(
                    probeStream,
                    new Mp4ProbeOptions
                    {
                        MaxMetadataBytes = options.MaxInputBytes,
                        MaxBoxPayloadBytes = options.MaxInputBytes
                    },
                    cancellationToken)
                .AsTask()
                .GetAwaiter()
                .GetResult();

            if (probe.Info is null || probe.Status == ProbeStatus.InvalidFormat)
            {
                warnings.Add("The input could not be probed as ISO-BMFF MP4.");
                return null;
            }

            if (probe.Status != ProbeStatus.Complete)
            {
                warnings.Add($"Probe status: {probe.Status}.");
            }

            return probe.Info;
        }
        catch (Exception error) when (
            error is Mp4FormatException or InvalidDataException or NotSupportedException)
        {
            warnings.Add($"Probe failed: {error.Message}");
            return null;
        }
    }

    private static List<TrackSummary> BuildTracks(
        ContainerInfo? container,
        List<BoxNode> boxes,
        List<string> warnings)
    {
        var tracks = new List<TrackSummary>();
        if (container is null)
        {
            return tracks;
        }

        var trackNodes = FindTracks(boxes);
        foreach (var track in container.Tracks)
        {
            var trackNode = trackNodes.FirstOrDefault(
                item => GetUInt32(FindChild(item, "tkhd"), "trackId") == track.Id.Value);
            var mediaHeader = FindChild(FindChild(trackNode, "mdia"), "mdhd");
            var duration = GetUInt64(mediaHeader, "duration");
            var timeScale = track.TimeScale != 0
                ? track.TimeScale
                : GetUInt32(mediaHeader, "timescale");
            var language = GetField(mediaHeader, "language");
            if (track is SubtitleTrackInfo subtitle && !string.IsNullOrEmpty(subtitle.Language))
            {
                language = subtitle.Language;
            }

            var summary = new TrackSummary
            {
                Id = track.Id.Value,
                Type = track.Type.ToString(),
                Codec = track.Codec.ToString(),
                TimeScale = timeScale,
                Duration = checked((long)Math.Min(duration, (ulong)long.MaxValue)),
                DurationSeconds = timeScale == 0 ? 0 : (double)duration / timeScale,
                Language = language,
                Width = track is VideoTrackInfo video ? video.Width : 0,
                Height = track is VideoTrackInfo videoTrack ? videoTrack.Height : 0,
                Channels = track is AudioTrackInfo audio ? audio.Channels : 0,
                SampleRate = track is AudioTrackInfo audioTrack ? audioTrack.SampleRate : 0
            };

            if (summary.Duration == 0 && trackNode is not null)
            {
                var headerDuration = GetUInt64(FindChild(trackNode, "tkhd"), "duration");
                summary = new TrackSummary
                {
                    Id = summary.Id,
                    Type = summary.Type,
                    Codec = summary.Codec,
                    TimeScale = summary.TimeScale,
                    Duration = checked((long)Math.Min(headerDuration, (ulong)long.MaxValue)),
                    DurationSeconds = summary.TimeScale == 0
                        ? 0
                        : (double)headerDuration / summary.TimeScale,
                    Language = summary.Language,
                    Width = summary.Width,
                    Height = summary.Height,
                    Channels = summary.Channels,
                    SampleRate = summary.SampleRate
                };
            }

            tracks.Add(summary);
        }

        if (tracks.Count == 0)
        {
            warnings.Add("No readable tracks were found.");
        }

        return tracks;
    }

    private static MovieSummary BuildMovieSummary(
        List<BoxNode> boxes,
        List<TrackSummary> tracks,
        List<string> warnings)
    {
        var fileType = FindTopLevel(boxes, "ftyp");
        var movieHeader = FindChild(FindTopLevel(boxes, "moov"), "mvhd");
        var timeScale = GetUInt32(movieHeader, "timescale");
        var duration = GetUInt64(movieHeader, "duration");
        var isFragmented = FindTopLevel(boxes, "moof") is not null ||
            FindChild(FindTopLevel(boxes, "moov"), "mvex") is not null;

        if (FindTopLevel(boxes, "moov") is null)
        {
            warnings.Add("No moov box was found.");
        }

        return new MovieSummary
        {
            MajorBrand = GetField(fileType, "majorBrand") ?? "",
            MinorVersion = GetUInt32(fileType, "minorVersion"),
            IsFragmented = isFragmented,
            Timescale = timeScale,
            Duration = duration,
            DurationSeconds = timeScale == 0 ? 0 : (double)duration / timeScale,
            TrackCount = tracks.Count
        };
    }

    private static List<SegmentInfo> BuildSegments(
        List<BoxNode> boxes,
        InspectOptions options,
        List<string> warnings)
    {
        var segments = new List<SegmentInfo>();
        if (!options.IncludeSegments || options.MaxSegments == 0)
        {
            return segments;
        }

        for (var index = 0; index < boxes.Count; index++)
        {
            var moof = boxes[index];
            if (moof.Type != "moof")
            {
                continue;
            }

            if (segments.Count >= options.MaxSegments)
            {
                warnings.Add($"Segment limit ({options.MaxSegments}) reached; remaining segments were skipped.");
                break;
            }

            var mdat = FindFollowingMdat(boxes, index + 1);
            var sequence = GetUInt32(FindChild(moof, "mfhd"), "sequenceNumber");
            var tracks = new List<uint>();
            var sampleCount = 0;
            var duration = 0L;
            var earliest = long.MaxValue;

            foreach (var traf in moof.Children.Where(static item => item.Type == "traf"))
            {
                var trackId = GetUInt32(FindChild(traf, "tfhd"), "trackId");
                if (trackId != 0 && !tracks.Contains(trackId))
                {
                    tracks.Add(trackId);
                }

                var decodeTimeNode = FindChild(traf, "tfdt");
                if (decodeTimeNode is not null)
                {
                    var decodeTime = GetUInt64(decodeTimeNode, "baseMediaDecodeTime");
                    if (decodeTime < (ulong)earliest)
                    {
                        earliest = checked((long)Math.Min(decodeTime, (ulong)long.MaxValue));
                    }
                }

                var trackDuration = 0L;
                foreach (var trun in traf.Children.Where(static item => item.Type == "trun"))
                {
                    var count = GetUInt32(trun, "sampleCount");
                    sampleCount += checked((int)Math.Min(count, int.MaxValue));
                    trackDuration += checked((long)Math.Min(GetUInt64(trun, "duration"),
                        (ulong)long.MaxValue));
                }

                if (trackDuration > duration)
                {
                    duration = trackDuration;
                }
            }

            if (earliest == long.MaxValue)
            {
                earliest = 0;
            }

            var mdatOffset = mdat?.Offset ?? 0;
            var mdatSize = mdat?.Size ?? 0;
            var size = mdat is null
                ? moof.Size
                : checked(mdat.Offset + mdat.Size - moof.Offset);

            segments.Add(new SegmentInfo
            {
                SequenceNumber = sequence,
                Offset = moof.Offset,
                Size = size,
                MoofOffset = moof.Offset,
                MdatOffset = mdatOffset,
                MdatSize = mdatSize,
                SampleCount = sampleCount,
                EarliestPresentationTime = earliest,
                Duration = duration
            });
            segments[^1].Tracks.AddRange(tracks);
        }

        return segments;
    }

    private static List<SampleRow> ReadSamples(
        Stream stream,
        InspectOptions options,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var samples = new List<SampleRow>();
        if (!options.IncludeSamples || options.MaxSamples == 0)
        {
            stream.Dispose();
            return samples;
        }

        try
        {
            using var sampleStream = stream;
            var reader = new Mp4Reader(
                sampleStream,
                new Mp4ProbeOptions
                {
                    MaxMetadataBytes = options.MaxInputBytes,
                    MaxBoxPayloadBytes = options.MaxInputBytes
                });

            while (samples.Count < options.MaxSamples)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sample = reader.ReadSampleAsync(cancellationToken)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
                if (sample is null)
                {
                    break;
                }

                samples.Add(new SampleRow
                {
                    Track = sample.Track.Value,
                    Pts = sample.Pts,
                    Dts = sample.Dts,
                    Duration = sample.Duration,
                    Size = sample.Data.Length,
                    KeyFrame = (sample.Flags & SampleFlags.KeyFrame) != 0
                });
            }

            if (samples.Count >= options.MaxSamples && options.MaxSamples != int.MaxValue)
            {
                warnings.Add($"Sample limit ({options.MaxSamples}) reached; remaining samples were skipped.");
            }
        }
        catch (Exception error) when (
            error is Mp4FormatException or InvalidDataException or NotSupportedException)
        {
            warnings.Add($"Sample reading failed: {error.Message}");
        }

        return samples;
    }

    private static List<BoxNode> FindTracks(List<BoxNode> boxes)
    {
        var result = new List<BoxNode>();
        var moov = FindTopLevel(boxes, "moov");
        if (moov is null)
        {
            return result;
        }

        result.AddRange(moov.Children.Where(static item => item.Type == "trak"));
        return result;
    }

    private static BoxNode? FindTopLevel(List<BoxNode> boxes, string type)
        => boxes.FirstOrDefault(item => item.Type == type);

    private static BoxNode? FindChild(BoxNode? parent, string type)
        => parent?.Children.FirstOrDefault(item => item.Type == type);

    private static BoxNode? FindFollowingMdat(List<BoxNode> boxes, int start)
    {
        for (var index = start; index < boxes.Count; index++)
        {
            if (boxes[index].Type == "moof")
            {
                return null;
            }

            if (boxes[index].Type == "mdat")
            {
                return boxes[index];
            }
        }

        return null;
    }

    private static string? GetField(BoxNode? node, string name)
        => node?.Fields.FirstOrDefault(item => item.Name == name)?.Value;

    private static uint GetUInt32(BoxNode? node, string name)
    {
        var value = GetField(node, name);
        return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : 0;
    }

    private static ulong GetUInt64(BoxNode? node, string name)
    {
        var value = GetField(node, name);
        return ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : 0;
    }

    private static void ValidateOptions(InspectOptions options)
    {
        if (options.MaxDepth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaxDepth));
        }

        if (options.MaxBoxes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaxBoxes));
        }

        if (options.MaxSamples < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaxSamples));
        }

        if (options.MaxSegments < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaxSegments));
        }

        if (options.MaxInputBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaxInputBytes));
        }
    }
}
