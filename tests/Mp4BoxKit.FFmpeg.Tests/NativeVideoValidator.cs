using FFmpeg.AutoGen;
using Xunit;

namespace Mp4BoxKit.FFmpeg.Tests;

internal static unsafe class NativeVideoValidator
{
    public static void AssertDecodes(byte[] file, int expectedFrames,
        AVCodecID expectedCodec = AVCodecID.AV_CODEC_ID_H264,
        IReadOnlyList<(byte[] Data, long Pts, long Dts)>? expectedPackets = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"fmp4-native-{Guid.NewGuid():N}.mp4");
        AVFormatContext* format = null;
        AVCodecContext* decoder = null;
        var packet = ffmpeg.av_packet_alloc();
        var frame = ffmpeg.av_frame_alloc();
        try
        {
            if (packet == null || frame == null)
                throw new OutOfMemoryException();
            File.WriteAllBytes(path, file);
            Check(ffmpeg.avformat_open_input(&format, path, null, null));
            Check(ffmpeg.avformat_find_stream_info(format, null));
            var index = ffmpeg.av_find_best_stream(format, AVMediaType.AVMEDIA_TYPE_VIDEO,
                -1, -1, null, 0);
            Check(index);
            var parameters = format->streams[index]->codecpar;
            Assert.Equal(expectedCodec, parameters->codec_id);
            var codec = ffmpeg.avcodec_find_decoder(parameters->codec_id);
            if (codec == null)
                throw new InvalidOperationException("H.264 decoder unavailable.");
            decoder = ffmpeg.avcodec_alloc_context3(codec);
            if (decoder == null)
                throw new OutOfMemoryException();
            Check(ffmpeg.avcodec_parameters_to_context(decoder, parameters));
            decoder->err_recognition = ffmpeg.AV_EF_EXPLODE;
            Check(ffmpeg.avcodec_open2(decoder, codec, null));
            var frames = 0;
            var packetIndex = 0;
            int result;
            while ((result = ffmpeg.av_read_frame(format, packet)) >= 0)
            {
                try
                {
                    if (packet->stream_index != index)
                        continue;
                    if (expectedPackets is not null)
                    {
                        Assert.True(packetIndex < expectedPackets.Count);
                        var expected = expectedPackets[packetIndex++];
                        var timeBase = format->streams[index]->time_base;
                        var sourceTimeBase = new AVRational { num = 1, den = 10 };
                        Assert.Equal(ffmpeg.av_rescale_q(expected.Pts, sourceTimeBase, timeBase), packet->pts);
                        Assert.Equal(ffmpeg.av_rescale_q(expected.Dts, sourceTimeBase, timeBase), packet->dts);
                        Assert.Equal(expected.Data, new ReadOnlySpan<byte>(packet->data, packet->size).ToArray());
                    }
                    Check(ffmpeg.avcodec_send_packet(decoder, packet));
                    frames += Drain(decoder, frame, frames);
                }
                finally { ffmpeg.av_packet_unref(packet); }
            }
            Assert.Equal(ffmpeg.AVERROR_EOF, result);
            if (expectedPackets is not null)
                Assert.Equal(expectedPackets.Count, packetIndex);
            Check(ffmpeg.avcodec_send_packet(decoder, null));
            frames += Drain(decoder, frame, frames);
            Assert.Equal(expectedFrames, frames);
        }
        finally
        {
            ffmpeg.av_frame_free(&frame);
            ffmpeg.av_packet_free(&packet);
            ffmpeg.avcodec_free_context(&decoder);
            ffmpeg.avformat_close_input(&format);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static int Drain(AVCodecContext* decoder, AVFrame* frame, int firstFrame)
    {
        var count = 0;
        while (true)
        {
            var result = ffmpeg.avcodec_receive_frame(decoder, frame);
            if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN) || result == ffmpeg.AVERROR_EOF)
                return count;
            Check(result);
            Assert.Equal(160, frame->width);
            Assert.Equal(96, frame->height);
            Assert.Equal(0, frame->decode_error_flags);
            Assert.Equal((int)AVPixelFormat.AV_PIX_FMT_YUV420P, frame->format);
            for (uint plane = 0; plane < 3; plane++)
            {
                var height = plane == 0 ? 96 : 48;
                var width = plane == 0 ? 160 : 80;
                var expected = (byte)(plane == 0 ? 16 + (firstFrame + count) * 8 : 128);
                for (var y = 0; y < height; y++)
                {
                    var row = new ReadOnlySpan<byte>(
                        frame->data[plane] + y * frame->linesize[plane], width);
                    for (var x = 0; x < row.Length; x++)
                        Assert.True(Math.Abs(row[x] - expected) <= 3,
                            $"Pixel mismatch: frame {firstFrame + count}, plane {plane}, ({x},{y}).");
                }
            }
            count++;
            ffmpeg.av_frame_unref(frame);
        }
    }

    private static void Check(int result)
    {
        if (result >= 0)
            return;
        var buffer = stackalloc byte[256];
        ffmpeg.av_strerror(result, buffer, 256);
        throw new InvalidOperationException(System.Runtime.InteropServices.Marshal
            .PtrToStringUTF8((IntPtr)buffer));
    }
}
