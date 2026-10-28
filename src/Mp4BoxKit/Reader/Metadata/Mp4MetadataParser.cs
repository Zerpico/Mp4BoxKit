using Mp4BoxKit.Boxes;

namespace Mp4BoxKit.Reader.Metadata;

internal static class Mp4MetadataParser
{
    public static bool Parse(
        ReadOnlySpan<byte> data,
        List<MediaTrackInfo> tracks)
    {
        var hasMvex = false;
        uint movieTimeScale = 1000;
        var headers = new IsoBoxReader(data);
        while (headers.TryRead(out var headerType, out var header))
        {
            if (headerType != BoxTypes.Mvhd)
            {
                continue;
            }

            if (header.Length < 4 || header[0] > 1)
            {
                throw new Mp4FormatException("Invalid mvhd version.");
            }
            var offset = header[0] == 1 ? 20 : 12;
            if (header.Length < offset + 4)
            {
                throw new Mp4FormatException("Truncated mvhd timescale.");
            }
            movieTimeScale = BigEndian.U32(header[offset..]);
            if (movieTimeScale == 0)
            {
                throw new Mp4FormatException("Zero movie timescale.");
            }
        }
        var boxes = new IsoBoxReader(data);
        while (boxes.TryRead(out var type, out var payload))
        {
            if (type == BoxTypes.Mvex)
            {
                hasMvex = true;
            }
            else if (type == BoxTypes.Trak)
            {
                var metadata = ParseTrack(payload);
                metadata.PresentationMediaStart = checked(metadata.PresentationMediaStart -
                    (long)((UInt128)metadata.EmptyEditDuration * metadata.TimeScale / movieTimeScale));
                if (metadata.Id != 0)
                {
                    tracks.Add(CreateTrackInfo(metadata));
                }
            }
        }

        return hasMvex;
    }

    public static TrackMetadata ParseTrackMetadata(ReadOnlySpan<byte> data)
        => ParseTrack(data);

    private static TrackMetadata ParseTrack(ReadOnlySpan<byte> data)
    {
        var metadata = new TrackMetadata();
        var boxes = new IsoBoxReader(data);
        while (boxes.TryRead(out var type, out var payload))
        {
            if (type == BoxTypes.Tkhd)
            {
                var trackIdOffset = payload.Length >= 1 && payload[0] == 1 ? 20 : 12;
                if (payload.Length >= trackIdOffset + 4)
                {
                    metadata.Id = BigEndian.U32(payload[trackIdOffset..]);
                    metadata.IsDefault = (BigEndian.U32(payload) & 1) != 0;
                }
            }
            else if (type == BoxTypes.Mdia)
            {
                ParseMedia(payload, metadata);
            }
            else if (type == new FourCc("edts"))
            {
                (metadata.PresentationMediaStart, metadata.EmptyEditDuration) = EditListParser.Parse(payload);
            }
        }

        return metadata;
    }

    private static void ParseMedia(
        ReadOnlySpan<byte> data,
        TrackMetadata metadata)
    {
        var boxes = new IsoBoxReader(data);
        while (boxes.TryRead(out var type, out var payload))
        {
            if (type == BoxTypes.Mdhd && payload.Length >= 16)
            {
                var version = payload[0];
                var timeScaleOffset = version == 1 ? 20 : 12;
                if (payload.Length >= timeScaleOffset + 4)
                    metadata.TimeScale = BigEndian.U32(payload[timeScaleOffset..]);
                var languageOffset = version == 1 ? 32 : 20;
                if (payload.Length >= languageOffset + 2)
                    metadata.Language = MediaLanguage.Unpack(
                        BigEndian.U16(payload[languageOffset..]));
            }
            else if (type == BoxTypes.Hdlr && payload.Length >= 12)
            {
                metadata.MediaType = GetMediaType(
                    new FourCc(BigEndian.U32(payload[8..])));
                if (payload.Length > 24)
                    metadata.Name = System.Text.Encoding.UTF8.GetString(payload[24..]).TrimEnd('\0');
            }
            else if (type == BoxTypes.Minf)
            {
                ParseMediaInformation(payload, metadata);
            }
        }
    }

    private static void ParseMediaInformation(
        ReadOnlySpan<byte> data,
        TrackMetadata metadata)
    {
        var boxes = new IsoBoxReader(data);
        while (boxes.TryRead(out var type, out var payload))
        {
            if (type == BoxTypes.Stbl)
            {
                ParseSampleTable(payload, metadata);
            }
        }
    }

    private static void ParseSampleTable(
        ReadOnlySpan<byte> data,
        TrackMetadata metadata)
    {
        var boxes = new IsoBoxReader(data);
        while (boxes.TryRead(out var type, out var payload))
        {
            if (type == BoxTypes.Stsd && payload.Length >= 16)
            {
                CodecSampleEntryParser.Parse(payload[8..], metadata);
            }
        }
    }

    private static MediaType GetMediaType(FourCc handler) =>
        handler.ToString() switch
        {
            "soun" => MediaType.Audio,
            "subt" or "sbtl" or "text" or "clcp" => MediaType.Subtitle,
            _ => MediaType.Video
        };

    public static MediaTrackInfo CreateTrackInfo(TrackMetadata metadata) =>
        metadata.MediaType switch
        {
            MediaType.Video => new VideoTrackInfo
            {
                Id = new TrackId(metadata.Id),
                PresentationMediaStart = metadata.PresentationMediaStart,
                Codec = metadata.Codec,
                TimeScale = metadata.TimeScale,
                Width = metadata.Width,
                Height = metadata.Height,
                CodecConfiguration = metadata.CodecConfiguration
            },
            MediaType.Audio => new AudioTrackInfo
            {
                PresentationMediaStart = metadata.PresentationMediaStart,
                Id = new TrackId(metadata.Id),
                Codec = metadata.Codec,
                TimeScale = metadata.TimeScale,
                Channels = metadata.Channels,
                BitsPerSample = metadata.BitsPerSample,
                PcmLittleEndian16 = metadata.PcmLittleEndian16,
                SampleRate = metadata.SampleRate,
                CodecConfiguration = metadata.CodecConfiguration
            },
            _ => new SubtitleTrackInfo
            {
                PresentationMediaStart = metadata.PresentationMediaStart,
                Name = metadata.Name,
                IsDefault = metadata.IsDefault,
                Language = metadata.Language,
                Id = new TrackId(metadata.Id),
                Codec = metadata.Codec,
                TimeScale = metadata.TimeScale,
                CodecConfiguration = metadata.CodecConfiguration
            }
        };
}
