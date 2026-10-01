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
    IImageThumbnailProcessor thumbnailProcessor,
    IProgressNotifier progressNotifier,
    ILogger<TranscodingPipeline> logger) : ITranscodingPipeline
{
    public async Task<ExitReason> ExecuteAsync(TranscodingJobInput job, CancellationToken ct)
    {
        var totalSw = Stopwatch.StartNew();
        using var workspace = new Workspace(job.VideoId);
        workspace.Create();

        // 1 Milestone message for the entire FFmpeg task
        await progressNotifier.NotifyAsync(job.TenantId, job.VideoId, "transcoding", "IN_PROGRESS");

        try
        {
            await RunStage("Download", job, ct, () =>
                transferService.DownloadAsync(job.SourcePath, workspace.SourceFile, ct));

            var renditions = RenditionLadder.Filter(
                job.Settings.Outputs,
                job.SourceMetadata.SourceWidth,
                workspace.IntermediatesDirectory);

            logger.LogInformation(
                "Filtered to {Count} renditions for {VideoId}: [{Renditions}]",
                renditions.Count, job.VideoId,
                string.Join(", ", renditions.Select(r => $"{r.Width}x{r.Height}")));

            await RunStage("Encode", job, ct, () =>
                encoder.EncodeAsync(
                    workspace.SourceFile,
                    workspace.IntermediatesDirectory,
                    renditions, ct));

            if (string.IsNullOrWhiteSpace(job.ThumbnailRelativeUrl))
            {
                await RunStage("Thumbnail_Extract", job, ct, async () =>
                {
                    await thumbnailGenerator.GenerateAsync(
                        workspace.SourceFile,
                        workspace.RawThumbnailFile,
                        job.SourceMetadata.Duration,
                        ct);
                        
                    await thumbnailProcessor.ProcessAsync(
                        workspace.RawThumbnailFile,
                        workspace.ThumbnailFile,
                        ct);
                });
            }
            else
            {
                await RunStage("Thumbnail_Custom", job, ct, async () =>
                {
                    logger.LogInformation("Looking up custom thumbnail for {VideoId} with prefix: {Url}",
                        job.VideoId, job.ThumbnailRelativeUrl);
                        
                    var customThumbnailKey = await transferService.FindFileByPrefixAsync(job.ThumbnailRelativeUrl, ct);
                    
                    if (string.IsNullOrEmpty(customThumbnailKey))
                    {
                        logger.LogWarning("Custom thumbnail not found for {VideoId}. Falling back to default extraction.", job.VideoId);
                        await thumbnailGenerator.GenerateAsync(
                            workspace.SourceFile,
                            workspace.RawThumbnailFile,
                            job.SourceMetadata.Duration,
                            ct);
                    }
                    else
                    {
                        logger.LogInformation("Downloading custom thumbnail {Key} -> {Local}", customThumbnailKey, workspace.RawThumbnailFile);
                        await transferService.DownloadAsync(customThumbnailKey, workspace.RawThumbnailFile, ct);
                    }

                    await thumbnailProcessor.ProcessAsync(
                        workspace.RawThumbnailFile,
                        workspace.ThumbnailFile,
                        ct);
                });
            }

            await RunStage("Package", job, ct, () =>
                packager.PackageAsync(
                    workspace.IntermediatesDirectory,
                    workspace.OutputDirectory,
                    renditions,
                    job.Settings,
                    job.Encryption,
                    ct));

            await RunStage("Upload", job, ct, () =>
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
        string stageName, TranscodingJobInput job,
        CancellationToken ct, Func<Task> action)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("[{Stage}] Starting for {VideoId}", stageName, job.VideoId);

        try
        {
            await action();
            sw.Stop();
            logger.LogInformation("[{Stage}] Completed for {VideoId} in {ElapsedSeconds:N1}s",
                stageName, job.VideoId, sw.Elapsed.TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("[{Stage}] Cancelled for {VideoId} after {ElapsedSeconds:N1}s",
                stageName, job.VideoId, sw.Elapsed.TotalSeconds);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "[{Stage}] Failed for {VideoId} after {ElapsedSeconds:N1}s",
                stageName, job.VideoId, sw.Elapsed.TotalSeconds);
            throw;
        }
    }
}
