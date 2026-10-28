using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace Mp4BoxKit.FFmpeg.Tests;

// Для библиотеки FFmpeg.AutoGen ОБЯЗАТЕЛЬНО нужно указать директорию с библиотеками для привязки
// иначе это просто не будет работать
// PLEASE!! Не трогайте здесь ничего

public class FFmpegLoader
{
    private const int AvLogBufferSize = 1 * 1024;
    private static bool _isInitialized;

    public static void Initialize()
    {
        if (_isInitialized)
            return;

        //ищем путь до библиотек ffmpeg
        _isInitialized = FindFFmpegDir(out var rootDir);

        if (!_isInitialized)
        {
            throw new Exception("Loading FFmpeg libraries failed");
        }

        if (string.IsNullOrEmpty(rootDir))
            throw new InvalidOperationException(
                "Set FFMPEG_ROOT to FFmpeg 6 shared libraries: " +
                "avcodec-60, avutil-58, avformat-60 " +
                "(plus their dependencies).");

        ffmpeg.RootPath = rootDir;
        DynamicallyLoadedBindings.Initialize();
    }

    public FFmpegLoader()
    {
        Initialize();
    }

    /// <summary>Ищет библиотеки ffmpeg нужных версий.</summary>
    /// <param name="logger">Логгер.</param>
    /// <param name="rootDir">Найденная папка с библиотеками.</param>
    /// <returns>true, если путь до библиотек найден.</returns>
    private static bool FindFFmpegDir(out string rootDir)
    {
        rootDir = string.Empty;
        bool result = false;
        string currentDir = AppContext.BaseDirectory;

        Dictionary<string, bool> foundLibraries = FunctionResolverBase.LibraryDependenciesMap
            .Where(x => x.Key != "postproc")
            .ToDictionary(k => k.Key, _ => false);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            result = FindForExternalPath(ref rootDir, foundLibraries);
        }
        return !result ? FindForInternalPath(ref rootDir, currentDir, foundLibraries) : result;
    }

    private static bool FindForExternalPath(ref string rootDir, Dictionary<string, bool> foundLibraries)
    {
        //на Linux будем искать ffmpeg в дистрибутиве 
        // следует прочитать файл /etc/ld.so.conf и файлЫ в папке /etc/ld.so.conf.d/ прочитать там пути и искать в них библиотеки ffmpeg
        const string globLdConfFile = "/etc/ld.so.conf";
        const string additionalLdConfDir = "/etc/ld.so.conf.d/";

        //читаем все файлы
        List<string> paths = [];
        if (File.Exists(globLdConfFile))
        {
            paths.AddRange(ParseConfigFile(globLdConfFile));
        }
        if (Directory.Exists(additionalLdConfDir))
        {
            foreach (var file in Directory.GetFiles(additionalLdConfDir))
            {
                paths.AddRange(ParseConfigFile(file));
            }
        }

        paths.AddRange(["/lib", "/lib64"]);
        var envFfmpegDir = Environment.GetEnvironmentVariable("MP4BOXKIT_FFMPEG_ROOT");
        if (envFfmpegDir != null)
            paths.Add(envFfmpegDir);

        foreach (var path in paths)
        {
            if (string.IsNullOrEmpty(path))
                continue;

            int keyIterator = 0;
            foreach (var library in FunctionResolverBase.LibraryDependenciesMap)
            {
                keyIterator++;
                //пробуем получить библиотеки ffmpeg с названием и версией
                var libraryName = library.Key;
                var libraryVersion = ffmpeg.LibraryVersionMap[libraryName];

                string fullLibraryName = string.Empty;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    fullLibraryName = GetNativeLibraryNameWindows(libraryName, libraryVersion);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    fullLibraryName = GetNativeLibraryNameLinux(libraryName, libraryVersion);

                if (string.IsNullOrEmpty(fullLibraryName))
                    return false;

                //пытаемся найти библиотеку ffmpeg в папке
                if (File.Exists(Path.Combine(path, fullLibraryName)))
                {
                    foundLibraries[library.Key] = true;
                    rootDir = path;
                }
            }
            if (foundLibraries.All(k => k.Value))
                return true;
        }
        return false;
    }

    private static IEnumerable<string> ParseConfigFile(string pathFile)
    {
        IList<string> lines = new List<string>();
        var reader = new StringReader(File.ReadAllText(pathFile));
        var line = reader.ReadLine();
        while (line != null)
        {
            line = line.TrimStart();
            if (line.Length != 0 && !line.StartsWith("#") && !line.StartsWith("include"))
            {
                lines.Add(line);
            }
            line = reader.ReadLine();
        }

        return lines;
    }

    private static bool FindForInternalPath(ref string rootDir, string currentDir, Dictionary<string, bool> foundLibraries)
    {
        foreach (var library in FunctionResolverBase.LibraryDependenciesMap)
        {
            //пробуем получить библиотеки ffmpeg с названием и версией
            var libraryName = library.Key;
            var libraryVersion = ffmpeg.LibraryVersionMap[libraryName];

            string fullLibraryName = string.Empty;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                fullLibraryName = GetNativeLibraryNameWindows(libraryName, libraryVersion);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                fullLibraryName = GetNativeLibraryNameLinux(libraryName, libraryVersion);

            if (string.IsNullOrEmpty(fullLibraryName))
                return false;

            //пытаемся найти библиотеку ffmpeg в текущей папке
            if (File.Exists(Path.Combine(currentDir, fullLibraryName)))
            {
                foundLibraries[library.Key] = true;
                rootDir = currentDir;
            }
            else
            {
                //пытаемся найти библиотеку ffmpeg в папке runtimes
                var osFolder = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" : "linux";
                if (File.Exists(Path.Combine(currentDir, "runtimes", $"{osFolder}-{GetNameBitProcess()}", "native", fullLibraryName)))
                {
                    foundLibraries[library.Key] = true;
                    rootDir = Path.Combine(currentDir, "runtimes", $"{osFolder}-{GetNameBitProcess()}", "native");
                }
            }
        }

        return foundLibraries.All(k => k.Value);
    }



    private static string GetNameBitProcess() => RuntimeInformation.ProcessArchitecture.ToString().ToLower();
    private static string GetNativeLibraryNameLinux(string libraryName, int version) => $"lib{libraryName}.so.{version}";
    private static string GetNativeLibraryNameWindows(string libraryName, int version) => $"{libraryName}-{version}.dll";
}
