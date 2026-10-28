using Mp4BoxKit.Boxes;
using Mp4BoxKit.Writer;

namespace Mp4BoxKit.Writer.Classic;

internal sealed class ClassicMovieWriter
{
    public byte[] Serialize(
        IReadOnlyList<MediaTrackInfo> tracks,
        IReadOnlyList<List<FileSample>> samples)
    {
        var buffer = new IsoBoxBuffer();
        IsoBoxWriter.Box(buffer, BoxTypes.Moov, movie =>
        {
            var movieDuration = tracks.Select((track, i) => GetTrackDuration(track,
                    i < samples.Count ? samples[i] : (IReadOnlyList<FileSample>)Array.Empty<FileSample>()))
                .DefaultIfEmpty(0u).Max();
            IsoBoxWriter.Box(movie, BoxTypes.Mvhd,
                box => MovieHeaderWriter.WriteMovieHeader(box, movieDuration, (uint)tracks.Count + 1));
            for (var i = 0; i < tracks.Count; i++)
            {
                var track = tracks[i];
                IReadOnlyList<FileSample> trackSamples = i < samples.Count
                    ? samples[i] : Array.Empty<FileSample>();
                IsoBoxWriter.Box(movie, BoxTypes.Trak,
                    box => WriteTrack(box, track, trackSamples));
            }
        });
        return buffer.ToArray();
    }

    private void WriteTrack(IsoBoxBuffer buffer, MediaTrackInfo track,
        IReadOnlyList<FileSample> samples)
    {
        var duration = samples.Count == 0 ? 0 :
            samples[^1].Dts + samples[^1].Duration - samples[0].Dts;
        IsoBoxWriter.Box(buffer, BoxTypes.Tkhd, box =>
        {
            box.WriteUInt32(track is SubtitleTrackInfo { IsDefault: false }
                ? 2u : IsoBmffConstants.TrackEnabledAndInMovie);
            box.WriteUInt32(0);
            box.WriteUInt32(0);
            box.WriteUInt32(track.Id.Value);
            box.WriteUInt32(0);
            box.WriteUInt32(GetTrackDuration(track, samples));
            box.WriteUInt64(0);
            box.WriteUInt16(0);
            box.WriteUInt16(0);
            box.WriteUInt16(track.Type == MediaType.Audio
                ? IsoBmffConstants.AudioTrackVolume : (ushort)0);
            box.WriteUInt16(0);
            MovieHeaderWriter.WriteIdentityMatrix(box);
            var video = track as VideoTrackInfo;
            var subtitle = track as SubtitleTrackInfo;
            box.WriteUInt32((uint)(video?.Width ?? subtitle?.Width ?? 0) << 16);
            box.WriteUInt32((uint)(video?.Height ?? subtitle?.Height ?? 0) << 16);
        });
        EditListWriter.Write(buffer, track, GetMovieDuration(track, samples));
        IsoBoxWriter.Box(buffer, BoxTypes.Mdia, media =>
        {
            WriteMediaHeader(media, track, duration);
            WriteHandler(media, track);
            IsoBoxWriter.Box(media, BoxTypes.Minf, information =>
            {
                MediaInformationWriter.WriteMediaHeader(information, track.Type);
                MediaInformationWriter.WriteDataInformation(information);
                new ClassicSampleTableBox(track, samples).WriteTo(information);
            });
        });
    }

    private static void WriteMediaHeader(IsoBoxBuffer buffer,
        MediaTrackInfo track, long duration)
    {
        IsoBoxWriter.Box(buffer, BoxTypes.Mdhd, box =>
        {
            box.WriteUInt32(0);
            box.WriteUInt32(0);
            box.WriteUInt32(0);
            box.WriteUInt32(track.TimeScale);
            box.WriteUInt32(checked((uint)Math.Max(0, duration)));
            box.WriteUInt16(track is SubtitleTrackInfo subtitle
                ? MediaLanguage.Pack(subtitle.Language) : IsoBmffConstants.UndeterminedLanguage);
            box.WriteUInt16(0);
        });
    }

    private static uint GetMovieDuration(MediaTrackInfo track, IReadOnlyList<FileSample> samples)
    {
        if (samples.Count == 0)
        {
            return 0;
        }
        var start = track.Timeline is { } timeline
            ? checked(timeline.PresentationStart - timeline.DecodeOrigin) : 0;
        var duration = Math.Max(0, checked(samples.Max(s => checked(s.Pts + s.Duration)) - start));
        // Round up so conversion never truncates the final presentation interval.
        return checked((uint)(((UInt128)(ulong)duration * 1000 + track.TimeScale - 1) / track.TimeScale));
    }

    private static uint GetTrackDuration(MediaTrackInfo track, IReadOnlyList<FileSample> samples)
    {
        var delay = track.Timeline?.PresentationDelay ?? 0;
        return checked(GetMovieDuration(track, samples) +
            (uint)((UInt128)(ulong)delay * 1000 / track.TimeScale));
    }

    private void WriteHandler(IsoBoxBuffer buffer, MediaTrackInfo track)
    {
        IsoBoxWriter.Box(buffer, BoxTypes.Hdlr, box =>
        {
            box.WriteUInt32(0);
            box.WriteUInt32(0);
            box.WriteUInt32(MovieHeaderWriter.GetHandlerType(track.Type));
            box.WriteUInt64(0);
            box.WriteUInt32(0);
            box.WriteBytes(System.Text.Encoding.UTF8.GetBytes(
                (track is SubtitleTrackInfo subtitle && subtitle.Name is not null
                    ? subtitle.Name : track.Type + "Handler") + "\0"));
        });
    }
}
