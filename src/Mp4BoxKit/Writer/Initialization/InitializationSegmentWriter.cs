using System.Buffers;
using System.Buffers.Binary;
using Mp4BoxKit.Boxes;
using Mp4BoxKit.Boxes.SampleEntries;
using static Mp4BoxKit.Boxes.MovieHeaderWriter;

namespace Mp4BoxKit.Writer.Initialization;

internal static class InitializationSegmentWriter
{
    internal static byte[] Serialize(
        IReadOnlyList<MediaTrackInfo> tracks)
    {
        var buffer = new IsoBoxBuffer();

        IsoBoxWriter.Box(buffer, BoxTypes.Ftyp, box =>
        {
            box.WriteUInt32(new FourCc("iso6").Value);
            box.WriteUInt32(1);
            box.WriteUInt32(new FourCc("iso6").Value);
            box.WriteUInt32(new FourCc("iso5").Value);
            box.WriteUInt32(new FourCc("isom").Value);
        });

        IsoBoxWriter.Box(buffer, BoxTypes.Moov, movie =>
        {
            IsoBoxWriter.Box(movie, BoxTypes.Mvhd,
                box => WriteMovieHeader(box, 0, (uint)tracks.Count + 1));

            foreach (var track in tracks)
            {
                IsoBoxWriter.Box(movie, BoxTypes.Trak, box => Trak(box, track));
            }

            IsoBoxWriter.Box(movie, BoxTypes.Mvex, movieExtends =>
            {
                foreach (var track in tracks)
                {
                    IsoBoxWriter.Box(movieExtends, BoxTypes.Trex,
                        box => Trex(box, track));
                }
            });
        });

        return buffer.ToArray();
    }
    private static void Trak(IsoBoxBuffer buffer, MediaTrackInfo track)
    {
        WriteInitTrackHeader(buffer, track);
        EditListWriter.Write(buffer, track);

        IsoBoxWriter.Box(buffer, BoxTypes.Mdia, media =>
        {
            WriteInitMediaHeader(media, track);
            WriteInitHandler(media, track);
            WriteInitMediaInformation(media, track);
        });
    }

    private static void WriteInitTrackHeader(
        IsoBoxBuffer buffer,
        MediaTrackInfo track)
    {
        IsoBoxWriter.Box(buffer, BoxTypes.Tkhd, box =>
        {
            box.WriteUInt32(track is SubtitleTrackInfo { IsDefault: false }
                ? 2u : IsoBmffConstants.TrackEnabledAndInMovie);
            box.WriteUInt32(0);
            box.WriteUInt32(0);
            box.WriteUInt32(track.Id.Value);
            box.WriteUInt32(0);
            box.WriteUInt32(0);
            box.WriteUInt64(0);
            box.WriteUInt16(0);
            box.WriteUInt16(0);
            box.WriteUInt16((ushort)(track.Type == MediaType.Audio ? IsoBmffConstants.AudioTrackVolume : 0));
            box.WriteUInt16(0);
            WriteIdentityMatrix(box);

            var video = track as VideoTrackInfo;
            var subtitle = track as SubtitleTrackInfo;
            box.WriteUInt32((uint)(video?.Width ?? subtitle?.Width ?? 0) << 16);
            box.WriteUInt32((uint)(video?.Height ?? subtitle?.Height ?? 0) << 16);
        });
    }

    private static void WriteInitMediaHeader(
        IsoBoxBuffer buffer,
        MediaTrackInfo track)
    {
        IsoBoxWriter.Box(buffer, BoxTypes.Mdhd, box =>
        {
            box.WriteUInt32(0);
            box.WriteUInt32(0);
            box.WriteUInt32(0);
            box.WriteUInt32(track.TimeScale);
            box.WriteUInt32(0);
            box.WriteUInt16(track is SubtitleTrackInfo subtitle
                ? MediaLanguage.Pack(subtitle.Language) : IsoBmffConstants.UndeterminedLanguage);
            box.WriteUInt16(0);
        });
    }

    private static void WriteInitHandler(
        IsoBoxBuffer buffer,
        MediaTrackInfo track)
    {
        IsoBoxWriter.Box(buffer, BoxTypes.Hdlr, box =>
        {
            box.WriteUInt32(0);
            box.WriteUInt32(0);
            box.WriteUInt32(GetHandlerType(track.Type));
            box.WriteUInt64(0);
            box.WriteUInt32(0);
            box.WriteBytes(System.Text.Encoding.UTF8.GetBytes(
                (track is SubtitleTrackInfo subtitle && subtitle.Name is not null
                    ? subtitle.Name : track.Type.ToString()) + "\0"));
        });
    }

    private static void WriteInitMediaInformation(
        IsoBoxBuffer buffer,
        MediaTrackInfo track)
    {
        IsoBoxWriter.Box(buffer, BoxTypes.Minf, mediaInformation =>
        {
            MediaInformationWriter.WriteMediaHeader(mediaInformation, track.Type);
            MediaInformationWriter.WriteDataInformation(mediaInformation);

            IsoBoxWriter.Box(mediaInformation, BoxTypes.Stbl, sampleTable =>
            {
                IsoBoxWriter.Box(sampleTable, BoxTypes.Stsd, description =>
                {
                    description.WriteUInt32(0);
                    description.WriteUInt32(1);
                    SampleEntryWriter.WriteTo(description, track);
                });

                WriteEmptySampleTable(sampleTable);
            });
        });
    }

    private static void WriteEmptySampleTable(IsoBoxBuffer buffer)
    {
        IsoBoxWriter.Box(buffer, BoxTypes.Stts, box =>
        {
            box.WriteUInt32(0);
            box.WriteUInt32(0);
        });
        IsoBoxWriter.Box(buffer, BoxTypes.Stsc, box =>
        {
            box.WriteUInt32(0);
            box.WriteUInt32(0);
        });
        IsoBoxWriter.Box(buffer, BoxTypes.Stsz, box =>
        {
            box.WriteUInt32(0);
            box.WriteUInt32(0);
            box.WriteUInt32(0);
        });
        IsoBoxWriter.Box(buffer, BoxTypes.Stco, box =>
        {
            box.WriteUInt32(0);
            box.WriteUInt32(0);
        });
    }


    private static void Trex(IsoBoxBuffer buffer, MediaTrackInfo track)
    {
        buffer.WriteUInt32(0);
        buffer.WriteUInt32(track.Id.Value);
        buffer.WriteUInt32(1);
        buffer.WriteUInt32(0);
        buffer.WriteUInt32(0);
        buffer.WriteUInt32(0);
    }
    // Builds a media fragment; trun carries per-sample duration/size/flags/CTO.
}
