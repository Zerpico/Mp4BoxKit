# Mp4BoxKit

Простая и лёгкая .NET-библиотека для чтения, записи и инспекции MP4/fMP4. Она рассчитана на потоковую запись: вы передаёте готовые access
units и codec configuration, а на выходе получаете classic MP4-файл или
самостоятельные сегменты fragmented MP4.

Основные сценарии — H.264/H.265 видео и AAC аудио. Reader также разбирает
смешанные classic/fragmented файлы, sample tables, edit lists, структуру
боксов и границы fMP4-сегментов. Ядро библиотеки не зависит от платформы и
совместимо с AOT; FFmpeg используется только в необязательном native-тестовом
проекте.

## Что умеет библиотека

- classic MP4, включая FastStart;
- fragmented MP4 с initialization, media и index частями;
- `sidx`, `mfra/tfra`, `styp`;
- синхронный вывод или асинхронный `IFmp4SegmentSink`;
- H.264/H.265 NAL framing и проверку `avcC`/`hvcC`;
- AAC-LC с автоматическим `esds` или явной codec configuration;
- разбор ADTS AAC;
- unified `Mp4Reader` для classic и fragmented MP4;
- инспекцию боксов, треков, сэмплов и fMP4-сегментов.

## Поддерживаемые кодеки

#### Видео
- H.264
- H.265
- AV1
- VP8
- VP9
- MPEG-4
- MPEG2
- MJPEG (частично)

#### Аудио
- AAC
- MP3
- Opus
- AC-3
- PCM (частично `ipcm/fpcm/lpcm/sowt/twos/in24/in32`)

#### Субтитры

- tx3g
- wvtt (только чтение)

## Чтение MP4

```csharp
using Mp4BoxKit;

using var input = File.OpenRead("input.mp4");
var reader = new Mp4Reader(input);

while (await reader.ReadSampleAsync() is { } sample)
{
    // sample.Track, sample.Pts, sample.Dts, sample.Duration,
    // sample.Flags, sample.Data
    Console.WriteLine($"{sample.Track} {sample.Pts} {sample.Data.Length}");
}
```

Reader сам определяет, classic это файл или fragmented. `Mp4Reader.Info`
содержит brand, layout и список треков.

## Запись classic MP4

```csharp
using Mp4BoxKit;
using Mp4BoxKit.Codecs;

var avcC = AvcConfiguration.CreateFromSpsPps(sps, pps);

using var output = File.Create("video.mp4");
using var writer = new Mp4FileWriter(
    new StreamMp4Output(output),
    new Mp4FileWriterOptions { FastStart = true });

var video = writer.AddVideo(new VideoTrackOptions(
    Codec.H264,
    Width: 1280,
    Height: 720,
    TimeScale: 90_000,
    CodecConfiguration: avcC));

writer.WriteSample(
    video,
    accessUnit,
    pts: 0,
    dts: 0,
    flags: SampleFlags.KeyFrame);

writer.Complete();
```

Каждый вызов `WriteSample` должен содержать один полный access unit.
H.264/H.265 samples — это length-prefixed NAL units, а не Annex B. Для Annex B:

```csharp
writer.WriteSample(
    video,
    AvcSample.FromAnnexB(annexB),
    pts,
    dts,
    SampleFlags.KeyFrame);
```

## Запись fragmented MP4

```csharp
using var output = File.Create("video-fragmented.mp4");

using var writer = new Mp4FragmentedWriter(
    new StreamMp4Output(output),
    new Mp4FragmentedWriterOptions
    {
        FragmentBoundary = Fmp4FragmentBoundary.EveryKeyFrame,
        WriteSegmentIndex = true,
        WriteRandomAccessIndex = true
    });

var video = writer.AddVideo(new VideoTrackOptions(
    Codec.H264,
    Width: 1280,
    Height: 720,
    TimeScale: 90_000,
    CodecConfiguration: avcC));

writer.WriteSample(video, accessUnit, pts, dts, SampleFlags.KeyFrame);
writer.FlushFragment();
writer.Complete();
```

Для сетевой доставки вместо seekable output передайте `IFmp4SegmentSink`:

```csharp
sealed class SegmentSink : IFmp4SegmentSink
{
    public ValueTask WriteAsync(Fmp4Segment segment, CancellationToken ct)
    {
        // segment.Kind: Initialization, Media или Index
        return SendAsync(segment.Data, ct);
    }
}

await using var writer = new Mp4FragmentedWriter(options, sink);
await writer.WriteSampleAsync(video, accessUnit, pts, dts, flags, ct);
await writer.CompleteAsync(ct);
```

`Fmp4Segment` содержит reference track, timescale, earliest PTS, duration,
keyframe flag и sample count.

## AAC

Для AAC-LC `esds` может быть сгенерирован из sample rate и channel count:

```csharp
var audio = writer.AddAudio(new AudioTrackOptions(
    Codec.Aac,
    Channels: 2,
    SampleRate: 48_000,
    TimeScale: 48_000));
```

Для HE-AAC/SBR/PS и других профилей передайте явную codec configuration:

```csharp
var esds = AacConfiguration.CreateEsdsFromAudioSpecificConfig(asc);
```

ADTS-фреймы можно преобразовать в raw AAC samples:

```csharp
if (!AacAdts.TryParse(frame, out _))
{
    throw new InvalidDataException("Invalid ADTS frame.");
}

var config = AacAdts.CreateEsds(frame);
var payload = AacAdts.GetPayload(frame);
writer.WriteSample(audio, payload, pts, dts, SampleFlags.KeyFrame, duration: 1024);
```

## Инспекция MP4

```csharp
using Mp4BoxKit.Inspection;

using var input = File.OpenRead("video.mp4");
var inspection = await Mp4Inspector.InspectAsync(
    input,
    "video.mp4",
    new InspectOptions
    {
        IncludeSamples = true,
        MaxSamples = 1000
    });

Console.WriteLine(inspection.Movie.MajorBrand);
Console.WriteLine(inspection.Movie.DurationSeconds);
Console.WriteLine(inspection.Tracks.Count);
Console.WriteLine(inspection.Boxes.Count);
Console.WriteLine(inspection.Segments.Count);

var json = InspectionJson.Serialize(inspection);
```

Результат содержит вложенное дерево боксов с offset и выбранными полями,
summary треков, метаданные сэмплов, `moof + mdat` сегменты и warnings при
достижении лимитов или ошибках формата.

`Inspect(byte* data, int length, ...)` доступен для unmanaged/WASM-буфера и
не копирует входные данные. Обычный managed overload принимает
`ReadOnlyMemory<byte>` и не делает лишнюю копию для array-backed памяти.

## CLI и GUI

### CLI

```powershell
dotnet run --project src/Mp4BoxKit.Cli -- video.mp4
dotnet run --project src/Mp4BoxKit.Cli -- video.mp4 --boxes --samples --segments
dotnet run --project src/Mp4BoxKit.Cli -- video.mp4 --json out.json --csv out/
```

Флаги:

```text
--summary --boxes --samples --segments
--depth --max-boxes --max-samples --max-segments --max-input-bytes
--json --csv --boxes-csv --samples-csv --segments-csv
```

Имя собранного executable: `boxkit`.

### Браузерные GUI

NativeAOT-LLVM используется для production-демо и GitHub Pages; Blazor — для
разработки и отладки. Оба используют общий UI из `demo`.

Общие файлы браузерного UI находятся в `demo`:

```text
demo/index.html
demo/app.js
demo/styles.css
demo/runtime.js
```

NativeAOT-сборка дополнительно создаёт:

```text
demo/Mp4BoxKit.NativeWasm.js
demo/Mp4BoxKit.NativeWasm.wasm
```

Эти два сгенерированных файла игнорируются Git; UI и runtime adapter являются
исходниками.

#### NativeAOT-LLVM

Нужны .NET 10, workload `wasm-tools` и Emscripten 3.1.56. Проект использует
пакеты из `dotnet-experimental` через корневой `NuGet.Config`.

```powershell
dotnet publish src/Mp4BoxKit.NativeWasm `
    -c Release -r browser-wasm -o demo
```

Запуск через любой static server:

```powershell
npx serve demo -l 8081
# или
python -m http.server 8081 --directory demo
# или
dotnet tool install --global dotnet-serve
dotnet serve -d demo -p 8081
```

Открыть `http://localhost:8081`. Модуль экспортирует C-ABI:
`mp4boxkit_alloc`, `mp4boxkit_inspect`, `mp4boxkit_free`.

#### Blazor WASM

Blazor — вариант для разработки и отладки. Он ссылается на
`Mp4BoxKit.Inspection` напрямую и вызывает его из общего JS UI через
`[JSInvokable]`:

```powershell
dotnet run --project src/Mp4BoxKit.Blazor -c Debug
```

Открыть `http://localhost:5081`. Blazor-specific `index.html` и `runtime.js`
лежат в `src/Mp4BoxKit.Blazor/wwwroot`; общие `app.js` и `styles.css`
копируются из `demo` во время сборки.

## Сборка и тесты

```powershell
dotnet restore Mp4BoxKit.slnx
dotnet build Mp4BoxKit.slnx -c Release
dotnet test Mp4BoxKit.slnx -c Release
dotnet format Mp4BoxKit.slnx whitespace --verify-no-changes
```

Native FFmpeg-тесты требуют FFmpeg 6 shared libraries:

```powershell
$env:MP4BOXKIT_FFMPEG_ROOT = "C:\path\to\ffmpeg-6\bin"
dotnet test Mp4BoxKit.slnx -c Release
```

## CI/CD

- `.github/workflows/ci.yml` — restore, проверка форматирования, тесты;
- `.github/workflows/pages.yml` — Windows runner, `wasm-tools`, прямой
  `dotnet publish` NativeAOT в `demo` и публикация GitHub Pages.

Скриптов сборки в репозитории нет: локальные команды и CI используют
`dotnet`/MSBuild напрямую.

## Ограничения

- Writer не кодирует медиа: он ожидает готовые access units и codec
  configuration от вызывающего кода.
- H.264/H.265 samples должны быть length-prefixed; Annex B конвертируется
  явно.
- NativeAOT-LLVM экспериментален и требует внешнего Emscripten или WASI SDK.
- Classic MP4 больше 4 GiB и полная совместимость с edit lists/плеерами не
  заявлены.
- Защита от malformed input и редкие codec profiles покрываются постепенно.
