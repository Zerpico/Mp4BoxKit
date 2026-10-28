using System.Globalization;
using System.Text;
using Mp4BoxKit.Inspection;

namespace Mp4BoxKit.Cli;

internal static class Cli
{
    public static async Task<int> RunAsync(string[] args)
    {
        CliOptions options;
        try
        {
            options = Parse(args);
        }
        catch (ArgumentException error)
        {
            Console.Error.WriteLine(error.Message);
            Console.Error.WriteLine();
            PrintHelp();
            return 2;
        }

        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        if (options.ShowVersion)
        {
            Console.WriteLine("Mp4BoxKit.Cli 0.1.0");
            return 0;
        }

        if (options.InputPath is null)
        {
            Console.Error.WriteLine("Input file is required.");
            PrintHelp();
            return 2;
        }

        try
        {
            await using var input = File.OpenRead(options.InputPath);
            var inspection = await Mp4Inspector.InspectAsync(
                input,
                Path.GetFileName(options.InputPath),
                options.ToInspectOptions());

            if (options.Summary)
            {
                PrintSummary(inspection);
            }

            if (options.Boxes)
            {
                PrintBoxes(inspection);
            }

            if (options.Samples)
            {
                PrintSamples(inspection);
            }

            if (options.Segments)
            {
                PrintSegments(inspection);
            }

            WriteJson(options.JsonPath, inspection);
            WriteCsv(options, inspection);
            return 0;
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or InvalidDataException
                or Mp4FormatException or ArgumentException)
        {
            Console.Error.WriteLine($"boxkit: {error.Message}");
            return 1;
        }
    }

    private static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        var sawView = false;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;
                case "-v":
                case "--version":
                    options.ShowVersion = true;
                    break;
                case "--summary":
                    options.Summary = true;
                    sawView = true;
                    break;
                case "--boxes":
                    options.Boxes = true;
                    sawView = true;
                    break;
                case "--samples":
                    options.Samples = true;
                    sawView = true;
                    break;
                case "--segments":
                    options.Segments = true;
                    sawView = true;
                    break;
                case "--json":
                    options.JsonPath = ReadValue(args, ref index, arg);
                    break;
                case "--csv":
                    options.CsvDirectory = ReadValue(args, ref index, arg);
                    break;
                case "--boxes-csv":
                    options.BoxesCsvPath = ReadValue(args, ref index, arg);
                    break;
                case "--samples-csv":
                    options.SamplesCsvPath = ReadValue(args, ref index, arg);
                    break;
                case "--segments-csv":
                    options.SegmentsCsvPath = ReadValue(args, ref index, arg);
                    break;
                case "--depth":
                    options.MaxDepth = ReadInt(args, ref index, arg);
                    break;
                case "--max-boxes":
                    options.MaxBoxes = ReadInt(args, ref index, arg);
                    break;
                case "--max-samples":
                    options.MaxSamples = ReadInt(args, ref index, arg);
                    options.Samples = true;
                    sawView = true;
                    break;
                case "--max-segments":
                    options.MaxSegments = ReadInt(args, ref index, arg);
                    options.Segments = true;
                    sawView = true;
                    break;
                case "--max-input-bytes":
                    options.MaxInputBytes = ReadLong(args, ref index, arg);
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        throw new ArgumentException($"Unknown option: {arg}");
                    }

                    if (options.InputPath is not null)
                    {
                        throw new ArgumentException("Only one input file is supported.");
                    }

                    options.InputPath = arg;
                    break;
            }
        }

        if (!sawView && !options.ShowHelp && !options.ShowVersion)
        {
            options.Summary = true;
            options.Boxes = true;
        }

        return options;
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for {option}.");
        }

        index++;
        return args[index];
    }

    private static int ReadInt(string[] args, ref int index, string option)
    {
        var value = ReadValue(args, ref index, option);
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var parsed))
        {
            throw new ArgumentException($"Invalid integer for {option}: {value}");
        }

        return parsed;
    }

    private static long ReadLong(string[] args, ref int index, string option)
    {
        var value = ReadValue(args, ref index, option);
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var parsed))
        {
            throw new ArgumentException($"Invalid integer for {option}: {value}");
        }

        return parsed;
    }

    private static void PrintSummary(Mp4Inspection inspection)
    {
        Console.WriteLine($"File: {inspection.FileName}");
        Console.WriteLine($"Size: {inspection.Size.ToString("N0", CultureInfo.InvariantCulture)} bytes");
        Console.WriteLine(
            $"Brand: {inspection.Movie.MajorBrand} (fragmented: {inspection.Movie.IsFragmented})");
        Console.WriteLine(
            "Movie: " +
            $"timescale={inspection.Movie.Timescale}, " +
            $"duration={inspection.Movie.Duration}, " +
            $"seconds={FormatSeconds(inspection.Movie.DurationSeconds)}, " +
            $"tracks={inspection.Movie.TrackCount}");

        if (inspection.Tracks.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Tracks:");
            Console.WriteLine("  id  type      codec      timescale  duration  seconds  details");
            foreach (var track in inspection.Tracks)
            {
                Console.WriteLine(
                    $"  {track.Id,-3} {track.Type,-9} {track.Codec,-10} " +
                    $"{track.TimeScale,-10} {track.Duration,-9} " +
                    $"{FormatSeconds(track.DurationSeconds),-8} {FormatTrackDetails(track)}");
            }
        }

        if (inspection.Warnings.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Warnings:");
            foreach (var warning in inspection.Warnings)
            {
                Console.WriteLine($"  - {warning}");
            }
        }

        Console.WriteLine();
    }

    private static void PrintBoxes(Mp4Inspection inspection)
    {
        Console.WriteLine("Boxes:");
        foreach (var box in inspection.Boxes)
        {
            PrintBox(box);
        }

        Console.WriteLine();
    }

    private static void PrintBox(BoxNode box)
    {
        var indent = new string(' ', box.Depth * 2);
        var fields = box.Fields.Count == 0
            ? ""
            : " " + string.Join(' ', box.Fields.Select(
                static field => $"{field.Name}={field.Value}"));
        var truncated = box.Truncated ? " truncated" : "";
        Console.WriteLine(
            $"{indent}{box.Type} @{box.Offset} size={box.Size} header={box.HeaderSize}" +
            $"{truncated}{fields}");

        foreach (var child in box.Children)
        {
            PrintBox(child);
        }
    }

    private static void PrintSamples(Mp4Inspection inspection)
    {
        Console.WriteLine("Samples:");
        Console.WriteLine("  track  pts       dts       duration  size      key");
        foreach (var sample in inspection.Samples)
        {
            Console.WriteLine(
                $"  {sample.Track,-5} {sample.Pts,-8} {sample.Dts,-8} " +
                $"{sample.Duration,-9} {sample.Size,-9} {sample.KeyFrame}");
        }

        Console.WriteLine();
    }

    private static void PrintSegments(Mp4Inspection inspection)
    {
        Console.WriteLine("Segments:");
        Console.WriteLine(
            "  seq  offset    size      moof      mdat      samples  duration  tracks");
        foreach (var segment in inspection.Segments)
        {
            Console.WriteLine(
                $"  {segment.SequenceNumber,-4} {segment.Offset,-9} {segment.Size,-9} " +
                $"{segment.MoofOffset,-9} {segment.MdatOffset,-9} " +
                $"{segment.SampleCount,-8} {segment.Duration,-9} " +
                $"{string.Join(',', segment.Tracks)}");
        }

        Console.WriteLine();
    }

    private static void WriteJson(string? path, Mp4Inspection inspection)
    {
        if (path is null)
        {
            return;
        }

        EnsureParentDirectory(path);
        File.WriteAllText(path, InspectionJson.Serialize(inspection), Encoding.UTF8);
    }

    private static void WriteCsv(CliOptions options, Mp4Inspection inspection)
    {
        if (options.CsvDirectory is not null)
        {
            Directory.CreateDirectory(options.CsvDirectory);
            WriteCsvFile(
                Path.Combine(options.CsvDirectory, "tracks.csv"),
                InspectionCsv.Tracks(inspection.Tracks));

            if (options.Boxes || options.JsonPath is not null)
            {
                WriteCsvFile(
                    Path.Combine(options.CsvDirectory, "boxes.csv"),
                    InspectionCsv.Boxes(inspection.Boxes));
            }

            if (options.Samples)
            {
                WriteCsvFile(
                    Path.Combine(options.CsvDirectory, "samples.csv"),
                    InspectionCsv.Samples(inspection.Samples));
            }

            if (options.Segments || options.JsonPath is not null)
            {
                WriteCsvFile(
                    Path.Combine(options.CsvDirectory, "segments.csv"),
                    InspectionCsv.Segments(inspection.Segments));
            }
        }

        WriteCsvFile(options.BoxesCsvPath, InspectionCsv.Boxes(inspection.Boxes));
        WriteCsvFile(options.SamplesCsvPath, InspectionCsv.Samples(inspection.Samples));
        WriteCsvFile(options.SegmentsCsvPath, InspectionCsv.Segments(inspection.Segments));
    }

    private static void WriteCsvFile(string? path, string content)
    {
        if (path is null)
        {
            return;
        }

        EnsureParentDirectory(path);
        File.WriteAllText(path, content, Encoding.UTF8);
    }

    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string FormatTrackDetails(TrackSummary track)
    {
        if (track.Width > 0 || track.Height > 0)
        {
            return $"{track.Width}x{track.Height}";
        }

        if (track.Channels > 0 || track.SampleRate > 0)
        {
            return $"{track.Channels}ch {track.SampleRate}Hz";
        }

        return track.Language ?? "";
    }

    private static string FormatSeconds(double seconds)
        => seconds.ToString("0.###", CultureInfo.InvariantCulture);

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            Usage: boxkit <input.mp4> [options]

            Views:
              --summary            Print file, movie, and track summary (default).
              --boxes              Print the ISO-BMFF box tree (default).
              --samples            Read and print sample metadata.
              --segments           Print fragmented moof+mdat segment summaries.

            Limits:
              --depth <n>          Maximum box depth (default: 12).
              --max-boxes <n>      Maximum boxes to walk (default: 10000).
              --max-samples <n>    Maximum samples to read (default: 1000).
              --max-segments <n>   Maximum segments to report (default: 1000).
              --max-input-bytes <n> Maximum input size (default: 536870912).

            Export:
              --json <path>        Write the full result as JSON.
              --csv <directory>    Write tracks/boxes/samples/segments CSV files.
              --boxes-csv <path>   Write the box tree as CSV.
              --samples-csv <path> Write samples as CSV.
              --segments-csv <path> Write segments as CSV.

            Other:
              -h, --help           Show this help.
              -v, --version        Show the version.
            """);
    }
}
