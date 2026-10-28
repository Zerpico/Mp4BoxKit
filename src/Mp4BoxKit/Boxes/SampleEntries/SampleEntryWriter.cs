namespace Mp4BoxKit.Boxes.SampleEntries;

using Mp4BoxKit.Boxes;
using Mp4BoxKit.Codecs;

internal static class SampleEntryWriter
{
    internal static void WriteTo(
        IsoBoxBuffer buffer,
        MediaTrackInfo track)
    {
        SampleEntryBox entry = track switch
        {
            VideoTrackInfo video => new VideoSampleEntryBox(video),
            AudioTrackInfo audio => new AudioSampleEntryBox(audio),
            _ => new GenericSampleEntryBox(track)
        };

        entry.WriteTo(buffer);
    }

    private abstract class SampleEntryBox
    {
        protected SampleEntryBox(MediaTrackInfo track) => Track = track;

        protected MediaTrackInfo Track { get; }

        protected abstract void WritePayload(IsoBoxBuffer buffer);

        public void WriteTo(IsoBoxBuffer buffer)
        {
            IsoBoxWriter.Box(buffer, CodecRegistry.GetSampleEntryType(Track), entry =>
            {
                entry.WriteBytes(new byte[6]);
                entry.WriteUInt16(1);
                WritePayload(entry);
                WriteCodecConfiguration(entry, Track);
            });
        }
    }

    private sealed class VideoSampleEntryBox(VideoTrackInfo track)
        : SampleEntryBox(track)
    {
        protected override void WritePayload(IsoBoxBuffer buffer)
        {
            buffer.WriteBytes(new byte[16]);
            buffer.WriteUInt16((ushort)track.Width);
            buffer.WriteUInt16((ushort)track.Height);
            buffer.WriteUInt32(IsoBmffConstants.VideoResolution);
            buffer.WriteUInt32(IsoBmffConstants.VideoResolution);
            buffer.WriteUInt32(0);
            buffer.WriteUInt16(IsoBmffConstants.VideoFrameCount);
            buffer.WriteBytes(new byte[32]);
            buffer.WriteUInt16(IsoBmffConstants.VideoDepth);
            buffer.WriteUInt16(IsoBmffConstants.VideoColorTable);
        }
    }

    private sealed class AudioSampleEntryBox(AudioTrackInfo track)
        : SampleEntryBox(track)
    {
        protected override void WritePayload(IsoBoxBuffer buffer)
        {
            buffer.WriteBytes(new byte[8]);
            buffer.WriteUInt16((ushort)track.Channels);
            buffer.WriteUInt16((ushort)track.BitsPerSample);
            buffer.WriteUInt16(0);
            buffer.WriteUInt16(0);
            buffer.WriteUInt32((uint)track.SampleRate << 16);
        }
    }

    private sealed class GenericSampleEntryBox(MediaTrackInfo track)
        : SampleEntryBox(track)
    {
        protected override void WritePayload(IsoBoxBuffer buffer)
        {
            if (Track.Codec == Codec.Tx3g)
            {
                buffer.WriteUInt32(0); // display flags
                buffer.WriteBytes([0, 0]); // horizontal / vertical justification
                buffer.WriteUInt32(0); // transparent background RGBA
                buffer.WriteUInt64(0); // default text box
                buffer.WriteUInt16(0); // style start
                buffer.WriteUInt16(0); // style end
                buffer.WriteUInt16(1); // font ID
                buffer.WriteBytes([0, 18]); // face flags and font size
                buffer.WriteUInt32(0xffffffff); // white text RGBA
                IsoBoxWriter.Box(buffer, new FourCc("ftab"), font =>
                {
                    font.WriteUInt16(1);
                    font.WriteUInt16(1);
                    font.WriteBytes([5, (byte)'A', (byte)'r', (byte)'i', (byte)'a', (byte)'l']);
                });
            }
        }
    }

    private static void WriteCodecConfiguration(
        IsoBoxBuffer buffer,
        MediaTrackInfo track)
    {
        if (track.CodecConfiguration.IsEmpty)
        {
            return;
        }

        var type = CodecConfigurationBoxes.ForCodec(track.Codec);

        IsoBoxWriter.Box(buffer, type, configuration =>
            configuration.WriteBytes(track.CodecConfiguration.Span));
    }
}
