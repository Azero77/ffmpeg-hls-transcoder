using System.Diagnostics;
using App.Interfaces;
using App.Models;
using Microsoft.Extensions.Logging;

namespace App.Pipeline;

/// <summary>
/// Orchestrates: Download → Filter → Encode → Thumbnail → Package → Upload.
/// Each stage is timed and logged with structured fields {Stage, VideoId, Duration}.
/// Returns <see cref="ExitReason"/> for the caller to map to a process exit code.
/// </summary>
public sealed class TranscodingPipeline(
    ITransferService transferService,
    ITranscoder encoder,
    IPackager packager,
    IThumbnailGenerator thumbnailGenerator,
    ILogger<TranscodingPipeline> logger) : ITranscodingPipeline
{
    public async Task<ExitReason> ExecuteAsync(TranscodingJobInput job, CancellationToken ct)
    {
        var totalSw = Stopwatch.StartNew();
        using var workspace = new Workspace(job.VideoId);
        workspace.Create();

        try
        {
            // 1. Download source
            await RunStage("Download", job.VideoId, ct, () =>
                transferService.DownloadAsync(job.SourcePath, workspace.SourceFile, ct));

            // 2. Filter rendition ladder to source resolution
            var renditions = RenditionLadder.Filter(
                job.Settings.Outputs,
                job.SourceMetadata.SourceWidth,
                workspace.IntermediatesDirectory);

            logger.LogInformation(
                "Filtered to {Count} renditions for {VideoId}: [{Renditions}]",
                renditions.Count, job.VideoId,
                string.Join(", ", renditions.Select(r => $"{r.Width}x{r.Height}")));

            // 3. Encode all renditions (parallel per FFmpegTranscoder)
            await RunStage("Encode", job.VideoId, ct, () =>
                encoder.EncodeAsync(
                    workspace.SourceFile,
                    workspace.IntermediatesDirectory,
                    renditions, ct));

            // 4. Thumbnail — only if no existing thumbnail URL was provided
            if (string.IsNullOrWhiteSpace(job.ThumbnailRelativeUrl))
            {
                await RunStage("Thumbnail", job.VideoId, ct, () =>
                    thumbnailGenerator.GenerateAsync(
                        workspace.SourceFile,
                        workspace.ThumbnailFile,
                        ct));
            }
            else
            {
                logger.LogInformation(
                    "Skipping thumbnail generation for {VideoId} — existing thumbnail: {Url}",
                    job.VideoId, job.ThumbnailRelativeUrl);
            }

            // 5. Package CMAF/fMP4 HLS (+ encryption if configured)
            await RunStage("Package", job.VideoId, ct, () =>
                packager.PackageAsync(
                    workspace.IntermediatesDirectory,
                    workspace.OutputDirectory,
                    renditions,
                    job.Settings,
                    job.Encryption,
                    ct));

            // 6. Upload output
            await RunStage("Upload", job.VideoId, ct, () =>
                transferService.UploadDirectoryAsync(
                    workspace.OutputDirectory,
                    job.OutputPrefix, ct));

            totalSw.Stop();
            logger.LogInformation(
                "Pipeline complete for {VideoId} in {ElapsedSeconds:N1}s",
                job.VideoId, totalSw.Elapsed.TotalSeconds);

            return ExitReason.Ok();
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Pipeline cancelled for {VideoId} after {Elapsed:N1}s",
                job.VideoId, totalSw.Elapsed.TotalSeconds);
            return ExitReason.Cancelled("Pipeline");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Pipeline failed for {VideoId} after {Elapsed:N1}s",
                job.VideoId, totalSw.Elapsed.TotalSeconds);
            return ExitReason.Failure("Pipeline", ex);
        }
    }

    private async Task RunStage(
        string stageName, Guid videoId,
        CancellationToken ct, Func<Task> action)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("[{Stage}] Starting for {VideoId}", stageName, videoId);

        try
        {
            await action();
            sw.Stop();
            logger.LogInformation("[{Stage}] Completed for {VideoId} in {ElapsedSeconds:N1}s",
                stageName, videoId, sw.Elapsed.TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("[{Stage}] Cancelled for {VideoId} after {ElapsedSeconds:N1}s",
                stageName, videoId, sw.Elapsed.TotalSeconds);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "[{Stage}] Failed for {VideoId} after {ElapsedSeconds:N1}s",
                stageName, videoId, sw.Elapsed.TotalSeconds);
            throw;
        }
    }
}
