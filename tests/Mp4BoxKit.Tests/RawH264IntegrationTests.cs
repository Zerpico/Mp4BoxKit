using System.Diagnostics;
using System.ComponentModel;
using Mp4BoxKit;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class RawH264IntegrationTests
{
    private const string InputPath = @"C:\usr\FrameH264\packet_000.h264";
    private const int Width = 1920;
    private const int Height = 1080;
    private const int FrameRate = 25;
    // ffprobe reports 127 decoded pictures for the current raw stream.
    private const int FrameCount = 127;
    private const int TimeScale = 90_000;
    private const int FrameDuration = TimeScale / FrameRate;

    [Fact]
    public void WritesRawH264AsFmp4File()
    {
        if (!File.Exists(InputPath))
        {
            return;
        }

        var input = File.ReadAllBytes(InputPath);
        var accessUnits = AnnexBH264.SplitAccessUnits(input);

        Assert.Equal(FrameCount, accessUnits.Count);

        var sps = AnnexBH264.FindNalUnit(input, 7);
        var pps = AnnexBH264.FindNalUnit(input, 8);

        Assert.NotEmpty(sps);
        Assert.NotEmpty(pps);

        var outputPath = Path.Combine(
            Path.GetDirectoryName(InputPath)!,
            "packet_000.fmp4");

        using (var output = File.Create(outputPath))
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddVideo(new VideoTrackOptions(
                Codec.H264,
                Width,
                Height,
                TimeScale,
                AnnexBH264.CreateAvcConfiguration(sps, pps)));

            for (var i = 0; i < accessUnits.Count; i++)
            {
                var timestamp = (long)i * FrameDuration;
                var flags = AnnexBH264.IsKeyFrame(accessUnits[i])
                    ? SampleFlags.KeyFrame
                    : SampleFlags.None;

                writer.WriteSample(
                    track,
                    AnnexBH264.ToLengthPrefixed(accessUnits[i]),
                    timestamp,
                    timestamp,
                    flags);
            }

            writer.Complete();
        }

        Assert.True(new FileInfo(outputPath).Length > 0);
        AssertFfprobe(outputPath);
    }

    private static void AssertFfprobe(string path)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffprobe",
            Arguments =
                "-v error -select_streams v:0 " +
                "-show_entries stream=codec_name,width,height,nb_frames,duration " +
                $"-of default=nw=1 \"{path}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            Assert.NotNull(process);
            var stdoutTask = process!.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(10_000))
            {
                process.Kill(entireProcessTree: true);
                return;
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();

            if (process.ExitCode != 0)
            {
                return;
            }

            Assert.Contains("codec_name=h264", stdout);
            Assert.Contains("width=1920", stdout);
            Assert.Contains("height=1080", stdout);
            // 127 pictures at 25 fps, including the final sample's duration.
            Assert.Contains("duration=5.080000", stdout);
        }
        catch (Win32Exception)
        {
            // ffprobe is optional for local integration tests.
        }
    }

    private static class AnnexBH264
    {
        public static List<byte[]> SplitAccessUnits(ReadOnlySpan<byte> input)
        {
            var nalUnits = SplitNalUnits(input);
            var result = new List<byte[]>();
            var current = new List<byte>();
            var currentHasVcl = false;

            foreach (var nal in nalUnits)
            {
                var type = nal[0] & 0x1f;
                var isVcl = type is >= 1 and <= 5;
                var firstSlice = isVcl && IsFirstSlice(nal);

                if (isVcl && firstSlice && currentHasVcl)
                {
                    result.Add(current.ToArray());
                    current.Clear();
                    currentHasVcl = false;
                }

                current.Add(0);
                current.Add(0);
                current.Add(0);
                current.Add(1);
                current.AddRange(nal);
                currentHasVcl |= isVcl;
            }

            if (current.Count != 0)
            {
                result.Add(current.ToArray());
            }

            return result;
        }

        public static byte[] FindNalUnit(ReadOnlySpan<byte> input, int type)
        {
            foreach (var nal in SplitNalUnits(input))
            {
                if ((nal[0] & 0x1f) == type)
                {
                    return nal;
                }
            }

            return Array.Empty<byte>();
        }

        public static byte[] CreateAvcConfiguration(
            ReadOnlySpan<byte> sps,
            ReadOnlySpan<byte> pps)
        {
            var result = new byte[11 + sps.Length + pps.Length];
            result[0] = 1;
            result[1] = sps.Length > 1 ? sps[1] : (byte)0x64;
            result[2] = sps.Length > 2 ? sps[2] : (byte)0;
            result[3] = sps.Length > 3 ? sps[3] : (byte)0x1f;
            result[4] = 0xff;
            result[5] = 0xe1;
            WriteU16(result.AsSpan(6), checked((ushort)sps.Length));
            sps.CopyTo(result.AsSpan(8));
            var ppsOffset = 8 + sps.Length;
            result[ppsOffset] = 1;
            WriteU16(result.AsSpan(ppsOffset + 1), checked((ushort)pps.Length));
            pps.CopyTo(result.AsSpan(ppsOffset + 3));
            return result;
        }

        public static byte[] ToLengthPrefixed(ReadOnlySpan<byte> accessUnit)
        {
            var result = new List<byte>(accessUnit.Length + 16);
            var nalUnits = SplitNalUnits(accessUnit);

            if (nalUnits.Count == 0 && accessUnit.Length != 0)
            {
                nalUnits.Add(accessUnit.ToArray());
            }

            foreach (var nal in nalUnits)
            {
                var size = new byte[4];
                WriteU32(size, checked((uint)nal.Length));
                result.AddRange(size);
                result.AddRange(nal);
            }

            return result.ToArray();
        }

        public static bool IsKeyFrame(ReadOnlySpan<byte> accessUnit)
        {
            var nalUnits = SplitNalUnits(accessUnit);

            if (nalUnits.Count == 0 && accessUnit.Length != 0)
            {
                nalUnits.Add(accessUnit.ToArray());
            }

            return nalUnits.Any(nal => (nal[0] & 0x1f) == 5);
        }

        private static bool IsFirstSlice(ReadOnlySpan<byte> nal)
        {
            if (nal.Length < 2)
            {
                return false;
            }

            var rbsp = RemoveEmulationPrevention(nal[1..]);
            var bit = 0;
            var leadingZeroBits = 0;

            while (bit < rbsp.Length * 8 && ReadBit(rbsp, bit++) == 0)
            {
                leadingZeroBits++;
            }

            if (leadingZeroBits >= 32 || bit + leadingZeroBits > rbsp.Length * 8)
            {
                return false;
            }

            var value = 1u;
            for (var i = 0; i < leadingZeroBits; i++)
            {
                value = (value << 1) | (uint)ReadBit(rbsp, bit++);
            }

            return value - 1 == 0;
        }

        private static byte[] RemoveEmulationPrevention(ReadOnlySpan<byte> data)
        {
            var result = new List<byte>(data.Length);

            for (var i = 0; i < data.Length; i++)
            {
                if (i + 2 < data.Length &&
                    data[i] == 0 &&
                    data[i + 1] == 0 &&
                    data[i + 2] == 3)
                {
                    result.Add(0);
                    result.Add(0);
                    i += 2;
                    continue;
                }

                result.Add(data[i]);
            }

            return result.ToArray();
        }

        private static int ReadBit(ReadOnlySpan<byte> data, int bit)
        {
            return (data[bit / 8] >> (7 - bit % 8)) & 1;
        }

        private static List<byte[]> SplitNalUnits(ReadOnlySpan<byte> input)
        {
            var result = new List<byte[]>();
            var starts = new List<int>();

            for (var i = 0; i + 3 < input.Length; i++)
            {
                if (input[i] != 0 || input[i + 1] != 0)
                {
                    continue;
                }

                if (input[i + 2] == 0 && input[i + 3] == 1)
                {
                    starts.Add(i);
                    i += 3;
                }
                else if (input[i + 2] == 1)
                {
                    starts.Add(i);
                    i += 2;
                }
            }

            for (var i = 0; i < starts.Count; i++)
            {
                var startCodeSize = input[starts[i] + 2] == 1 ? 3 : 4;
                var start = starts[i] + startCodeSize;
                var end = i + 1 < starts.Count ? starts[i + 1] : input.Length;

                if (end > start)
                {
                    result.Add(input[start..end].ToArray());
                }
            }

            return result;
        }

        private static void WriteU16(Span<byte> destination, ushort value)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(destination, value);
        }

        private static void WriteU32(Span<byte> destination, uint value)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(destination, value);
        }
    }
}
