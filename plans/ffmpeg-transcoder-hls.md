# Plan: `ffmpeg-transcoder-hls` — Standalone Fargate Transcoding Worker

> Extracted from `alphazero-api/Modules/VideoUploading/Infrastructure/Consumers/FFmpegTranscodingConsumer.cs`.
> Consumed as a **Step Functions step** (`arn:aws:states:::ecs:runTask.sync`) by the API repo's ingestion state machine. All sibling steps (analyze, CDN sync, publish, fail) remain Lambdas in the API repo.

## 1. Purpose & contract

A run-once console app, containerized, that: downloads a source video from S3, encodes an ABR ladder with **FFmpeg**, packages **CMAF/fMP4 HLS** (parity with the API's MediaConvert `job.json`) with optional raw-key CENC encryption, uploads to S3, and exits.

**Completion contract (AWS-verified):** `runTask.sync` polls `DescribeTasks` + ECS events. Container exit `0` → step succeeds; non-zero or task `Failures` → `States.TaskFailed` → `Catch` in the state machine. No task tokens, no lost jobs on OOM/deploy (replaces the in-process MassTransit job consumer).

**Sizing assumption:** sources ≤ 2 h / 10 GB (drives ephemeral storage + Fargate sizing in §9).

## 2. Repository layout

```
ffmpeg-transcoder-hls/
├── src/
│   └── AlphaZero.Workers.FFmpegTranscoder.Hls/
│       ├── AlphaZero.Workers.FFmpegTranscoder.Hls.csproj       ← TargetFramework net10.0
│       ├── appsettings.json
│       ├── Program.cs                                    ← Generic Host
│       ├── Dockerfile
│       └── Pipeline/
│           ├── TranscodingPipeline.cs                  ← orchestrates steps, owns exit code
│           ├── S3TransferService.cs                    ← IS3TransferService (AWSSDK.S3 TransferUtility)
│           ├── FFProbeAnalyzer.cs                     ← IVideoAnalyzer (FFMpegCore)
│           ├── FFmpegEncoder.cs                      ← IVideoEncoder (CliWrap)
│           ├── ShakaPackager.cs                      ← IPackager (CliWrap + packager binary)
│           └── FFmpegThumbnailGenerator.cs           ← IThumbnailGenerator (CliWrap)
│
├── plans/ffmpeg-transcoder-hls.md                      ← THIS FILE (plan reference)
└── README.md                                          ← repository overview
```

**Why this structure:**
- Top-level repo (not inside `alphazero-api/Modules/`), zero EF/Application/Domain dependencies — pure infrastructure worker.
- NuGet only: `AWSSDK.S3`, `FFMpegCore`, `CliWrap`, `ErrorOr`, `Serilog` (OpenTelemetry optional).
- The `Pipeline/` folder mirrors the orchestration order; each class implements an interface so the pipeline can be faked or swapped.

## 3. Job input contract — “similar to job.json”

Same rendition ladder as the MediaConvert `job.json` so **both branches are config-driven** (MediaConvert Fargate job vs. this FFmpeg Fargate worker):

```csharp
public sealed record TranscodingJobInput(
    Guid VideoId,                      ← SFN passes as JSON; also the VideoId used for TempFolder
    string SourceKey,                  ← s3://input-bucket/{tenantId}/{videoId}/{file}
    string OutputPrefix,               ← s3://output-bucket/streaming/{tenantId}/{videoId}/
    int SourceWidth, int SourceHeight,
    string EncryptionMethod,           ← "None" | "ClearKey"
    TranscodeSettings Settings);       ← below

public sealed record TranscodeSettings(
    OutputPreset[] Outputs,            // { Width, Height, MaxBitrate, QvbrQualityLevel, NameModifier }
    int SegmentLength = 6, int FragmentLength = 2,
    AudioSettings Audio = default);    // { Codec = "AAC", Bitrate = 64000, SampleRate = 44100 }

public sealed record OutputPreset(int Width, int Height, int MaxBitrate, int QvbrQualityLevel, string NameModifier);
public sealed record AudioSettings(string Codec, int Bitrate, int SampleRate);
```

**Rendition filtering** (`width <= sourceWidth`, else keep lowest) is ported from `MediaConvertTranscodingService.cs:111-125` and lives in `Pipeline/TranscodingPipeline.cs` — not in the input record itself.

## 4. Host implementation — Generic Host, run-once, exit-code contract

```csharp
// Program.cs
var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddEnvironmentVariables("TRANSCODER__");
builder.Services.Configure<TranscoderOptions>(builder.Configuration.GetSection("Transcoder"));
builder.Services.AddAWSService<IAmazonS3>();
builder.Services.AddSingleton<IS3TransferService, S3TransferService>();
builder.Services.AddSingleton<IVideoAnalyzer, FFProbeAnalyzer>();
builder.Services.AddSingleton<IVideoEncoder, FFmpegEncoder>();
builder.Services.AddSingleton<IPackager, ShakaPackager>();
builder.Services.AddSingleton<IThumbnailGenerator, FFmpegThumbnailGenerator>();
builder.Services.AddSingleton<TranscodingPipeline>();
builder.Logging.ClearProviders().AddSimpleConsole(o => {
    o.SingleLine = true;
    o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
});
builder.Services.AddSerilog();

var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
int exitCode;
try
{
    // SFN passes JSON on the command line or via /tmp/input.json; here we read from stdin for brevity
    using var reader = new StreamReader(Console.OpenStandardInput());
    var inputJson = reader.ReadToEnd();
    var input = JsonSerializer.Deserialize<TranscodingJobInput>(
        inputJson, JsonSerializerOptions.Web) ?? throw new InvalidOperationException("Empty SFN job input");

    exitCode = await host.Services.GetRequiredService<TranscodingPipeline>()
        .RunAsync(input, lifetime.ApplicationStopping);
}
catch (OperationCanceledException)
{
    logger.LogWarning("SIGTERM received; exiting gracefully.");
    exitCode = (int)ExitCode.Cancelled;
}
catch (Exception ex)
{
    logger.LogError(ex, "Transcoding failed");
    exitCode = (int)ExitCode.Failed;
}
finally
{
    await host.StopAsync();
}

return exitCode;

public enum ExitCode { Success = 0, Failed = 1, Cancelled = 2, InvalidInput = 3 }
```

Notes on host:
- **Input via stdin** (SFN `ContainerOverrides.Command` passes JSON string; a tiny `bootstrap.sh` writes it to `/tmp/input.json` then execs dotnet; avoids `ARG_MAX` limits and avoids logging secrets to task metadata).
- **Graceful SIGTERM:** Fargate task stop timeout is 30 s; `CancellationToken` linked to `ApplicationStopping` flows into every pipeline stage; `CliWrap` cancelling → process receives SIGTERM → ffmpeg/packager shut down cleanly.
- **Redacted logging:** any echo of the input JSON omits `KeyValue`, `KeyId`, `KeyUrl` fields (raw-key encryption params); never surface them in CloudWatch.

## 5. Pipeline steps (orchestration order)

```csharp
public sealed class TranscodingPipeline(
    IS3TransferService s3,
    IVideoAnalyzer analyzer,
    IVideoEncoder encoder,
    IPackager packager,
    IThumbnailGenerator thumbs,
    IOptions<TranscoderOptions> opts,
    ILogger<TranscodingPipeline> log)
{
    public async Task<int> RunAsync(TranscodingJobInput job, CancellationToken ct)
    {
        using var work = new TempWorkspace(job.VideoId); // cleans /tmp/transcode/{VideoId} in finally

        // 1. Download source from S3 (resumable TransferUtility)
        await s3.DownloadAsync(job.SourceKey, work.SourceFile, ct);

        // 2. Probe source dimensions/duration (FFProbe via FFMpegCore)
        var probe = await analyzer.AnalyzeAsync(work.SourceFile, ct);

        // 3. Filter/reduce rendition ladder to source width (port from MediaConvertTranscodingService.cs:111-125)
        var rends = RenditionLadder.Filter(job.Settings.Outputs, probe.Width);

        // 4. Encode each rendition in parallel (FFmpeg CliWrap)
        await encoder.EncodeAsync(work.SourceFile, work.Intermediates, rends, probe, ct);

        // 5. Package CMAF/fMP4 + master playlists (Shaka Packager; raw-key CENC if encrypted)
        await packager.PackageAsync(work.Intermediates, work.PackagedDir, job.Settings, ct);

        // 6. Upload output dir to S3 (overwrite=false then verify)
        await s3.UploadDirectoryAsync(work.PackagedDir, job.OutputPrefix, ct);

        log.LogInformation("Done {VideoId} in {Duration:N1}s", job.VideoId, (ct.Elapsed - work.Start).Value.TotalSeconds);
        return (int)ExitCode.Success;
    }
    // Every step has a try/catch → structured {Stage,VideoId,Reason} log + rethrow → caller maps to SFN failure.
    // TempWorkspace.Dispose deletes /tmp/transcode/{VideoId} in finally.
}
```

## 6. FFmpeg encode — CliWrap, production-grade args (replaces the old `BuildFFmpegArgs` in `FFmpegTranscodingConsumer.cs:156-211`)

Key differences vs. the old consumer arg string, and why:

- **One pass per rendition** (`map 0:v:0 -map 0:a:0` + scale) instead of `-var_stream_map`: avoids fragile stream-map syntax, lets each encode run on its own core, and pairs naturally with Shaka Packager which re-maps streams later.
- **`-preset medium`, `-crf {18|21|23}` + `-maxrate/-bufsize`** — CRF-capped = QVBR-equivalent quality targeting, matching `job.json`'s rate control intent.
- **Audio once per rendition via `-c:a aac -b:a 128k -ar 44100`** in the encode pass; **no loudnorm duplicate work** (loudnorm re-encoded 4× before if you ran per-rendition; instead we keep a single AAC encode and let the packager's `-filter:a "loudnorm"` handle normalization at packager time, matching `job.json` output).
- **`-pix_fmt yuv420p`, `-movflags +faststart`** on intermediates (universal player compatibility; faststart for progressive fallback).
- **No `-hls_key_info_file`** — encryption moves to the packager (CENC), fixing the MPEG-TS/CMAF mismatch the old consumer had.
- **Thumbnails:** separate `-ss 5 -frames:v 1 -q:v 2 poster.jpg` (validated seek is slow; position before `-i` for keyframe-seek speed).

```csharp
// FFmpegEncoder.cs public async Task EncodeAsync(string src, string outDir, ...)
// Inside, Parallel.ForEachAsync renditions with args builder:
// args = "-nostdin -hide_banner -y -i " + src +
//        "-map 0:v:0 -map 0:a:0 -c:v libx264 -preset medium -crf " + crf +
//        "-maxrate " + maxrate + "k -bufsize " + bufsize + "k" +
//        "-vf " + "scale=w=" + width + ":h=" + height + ":force_original_aspect_ratio=decrease" +
//        "-pix_fmt yuv420p -c:a aac -b:a 128k -ar 44100 -movflags +faststart" +
//        " " + outFile;
```

## 7. Shaka Packager — CMAF parity with `job.json`

The `job.json` produces CMAF: 6 s segments / 2 s fragments, `iframe_only_manifests`, AAC 64k/44.1k stereo, loudness −24 LKFS, static-key encryption with key URI + KMS SSE on output. Shaka equivalent (raw key provider) — build argument list per docs:

```bash
packager \
  'in=1080p.mp4,stream=video,init_segment=v_1080/init.mp4,segment_template=v_1080/$Number$.m4s,playlist_name=v_1080.m3u8,iframe_playlist_name=v_1080_iframe.m3u8,drm_label=HD' \
  'in=720p.mp4,stream=video,...,drm_label=SD' \
  'in=480p.mp4,...,drm_label=SD' \
  'in=360p.mp4,...,drm_label=SD' \
  'in=audio.mp4,stream=audio,init_segment=audio/init.mp4,segment_template=audio/$Number$.m4s,playlist_name=audio.m3u8,hls_group_id=audio,hls_name=English' \
  --hls_master_playlist_output master.m3u8 \
  --hls_playlist_type VOD \
  --hls_segment_duration 6 \
  --hls_fragment_duration 2 \
  --hls_flags independent_segments+iframes_only \
  --enable_raw_key_encryption \
  --keys label=HD:key_id=6d76f25cb17f5e16b8eaef6bbf582d8e:key=cb541084c99731aef4fff74500c12ead \
  --keys label=SD:key_id=abba271e8bcf552bbd2e86a434a9a5d9:key=69eaa802a6763af979e8d1940fb88392 \
  --keys label=AUDIO:key_id=f3c5e0361e6654b28f8049c778b23946:key=a4631a153a443df9eed0593043db7519 \
  --mpd_output master.mpd   ← optional DASH sidecar
```

The **arguments are generated in `ShakaPackager.cs`** from the `TranscodeSettings` + `AudioSettings` + `EncryptionMethod`; no raw `packager` CLI invocation in code—`System.Diagnostics.Process` is replaced with `CliWrap`, same pattern as the encode step.

## 8. S3 transfer — `IS3TransferService` / `S3TransferService`

Minimal wrapper around `AmazonS3TransferUtility` (or `PutObjectAsync` + `HeadObject` for idempotency) — keeps the worker thin and testable. Interface defines:

```csharp
public interface IS3TransferService
{
    Task DownloadAsync(string sourceKey, string localPath, CancellationToken ct);
    Task UploadDirectoryAsync(string localDir, string s3Prefix, CancellationToken ct);
}
```

Implementation uses `TransferUtility.UploadDirectoryAsync` (existing API pattern at `S3VideoCdnSyncService.cs:129-137`). Temp folder path: `/tmp/transcode/{VideoId}`; Fargate 1.4+ gives 20 GiB ephemeral storage free (encrypts with AWS-owned key or customer CMK).

## 9. Fargate sizing

| Source tier | Max duration | Max size | Ephemeral storage | vCPU | Memory |
|---|---|---|---|---|---|
| **Small** (default) | 2 h | 10 GB | 20 GiB (default Fargate, AWS-owned) | 2 | 4 GiB |
| **Large** (if needed) | 4 h | 25 GB | up to 200 GiB (specify `ephemeralStorage` in task definition) | 4 | 8 GiB |

- **Why 20 GiB default is fine:** compressed+uncompressed video artifacts for a 2 h 10 GB source ≤ 10 GB + temp workspace ≤ 2 GB < 20 GiB.
- If you later raise the tier to 4 h / 25 GB, add `ephemeralStorage` to the Fargate task definition (AWS doc: version 1.4.0+; up to 200 GiB).

## 10. Step Functions integration (API repo — NOT in this repo)

Add a new choice step after the existing `AnalyzeVideo` Lambda:

```
Choice: transcodingMethod
  "FFmpeg": arn:aws:states:::ecs:runTask.sync(
    "AlphaZero.Workers.FFmpegTranscoder.Hls",
    "ContainerOverrides": { "ContainerOverrides": [{ "Name": "mycontainer", "Command": ["dotnet", "run", "--", "/tmp/input.json"] }] },
    "LaunchType": "FARGATE",
    "NetworkConfiguration": { "AwsvpcConfiguration": { "Subnets": [...], "SecurityGroups": [...], "AssignPublicIp": "DISABLED" } }
  )
  "MediaConvert": arn:aws:states:::mediaconvert:createJob.sync( ... )
```

The `ContainerOverrides.Command` format matches the AWS docs (`Run a Job (.sync)` section). The SFN then catches `States.TaskFailed` → `MarkVideoAsFailed` Lambda (same as today).

**No code changes to the API repo beyond this SFN addition** — the console app exits 0/non-zero and the state machine wires the rest.

## 11. Migration checklist (from today to tomorrow)

| # | Item | Owner |
|---|---|---|
| 1 | Scaffold `ffmpeg-transcoder-hls` repo with plan above | Platform / infra |
| 2 | Implement `Program.cs`, `TranscodingPipeline.cs`, each `I*` + impl in `Pipeline/` | Dev |
| 3 | Write Dockerfile: `mcr.microsoft.com/dotnet/sdk:10` → `aspnet:10` final, install `ffmpeg`, `shaka-packager` binary to `/usr/local/bin`, copy `FFOptions.BinaryFolder` env | DevOps |
| 4 | Implement `IS3TransferService` / `S3TransferService` (or reuse existing `S3VideoCdnSyncService` code) | Dev |
| 5 | NuGet: `AWSSDK.S3`, `FFMpegCore`, `CliWrap`, `ErrorOr`, `Serilog` (or `Aspire.Shared` for AWS creds) | Dev |
| 6 | Add `Project("ffmpeg-transcoder-hls")` to `AlphaZero.sln` (or separate `.sln` for the worker) | Build |
| 7 | In API repo, add Step Functions choice step after `AnalyzeVideo` Lambda (see §10) | API team |
| 8 | Update `VideoUploadingModule.cs` `RegisterGlobal` / `RegisterPrivate` to register `StepFunctionsTranscodingService : IVideoTranscodingService` alongside existing `MediaConvertTranscodingService` (optional — just for compile, the SFN path does not use MassTransit) | API team |
| 9 | Add tests: `TranscodingPipeline` with unit mocks for S3, FFProbe, CliWrap, Packager; integration test with a 2-min source in a temp bucket | QA |
| 10 | Deploy worker Docker image to ECR; update Fargate task definition; test end-to-end with a small source | Release |

## 12. Remaining questions (answer before start)

1. **CMAF encryption method:** raw-key CENC (as above) vs. MediaConvert's static-key with KMS SSE? Your earlier answer chose **CMAF parity** — the plan assumes raw-key CENC that the packager signs with `--enable_raw_key_encryption --keys ...`. If you later need DRM (Widevine/PlayReady), swap the packager flags; the worker's `EncryptionMethod` field stays the same.
2. **Thumbnail strategy:** separate `-ss 5` capture (simple; no seeking) or keep the consumer's `hqdn3d + scale + pad`? Plan assumes a single poster frame; if you need `hqdn3d` noise reduction add it as a FFmpeg video filter before packager; the worker currently does not apply `hqdn3d`.
3. **Probe reliability:** the old consumer passed `SourceWidth/SourceHeight` from the saga state (set at analyze time). The new worker probes fresh via `FFProbe.AnalyseAsync` each run — eliminates stale dimension bugs but adds a tiny latency (< 1 s). Both are acceptable; this plan opts for fresh probe.