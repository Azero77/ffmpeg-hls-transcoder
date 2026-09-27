using Amazon.S3;
using Amazon.S3.Transfer;
using App.Interfaces;
using App.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace App.Pipeline;

public sealed class S3TransferService(
    IAmazonS3 s3Client,
    IOptions<TranscoderOptions> options,
    ILogger<S3TransferService> logger) : ITransferService
{
    private readonly TransferUtility _transferUtility = new(s3Client, new TransferUtilityConfig
    {
        ConcurrentServiceRequests = 8
    });

    public async Task DownloadAsync(string sourcePath, string destinationFilePath, CancellationToken ct)
    {
        var bucket = options.Value.S3.InputBucket;
        logger.LogInformation("Downloading s3://{Bucket}/{Key} → {Local}", bucket, sourcePath, destinationFilePath);

        var request = new TransferUtilityDownloadRequest
        {
            BucketName = bucket,
            Key = sourcePath,
            FilePath = destinationFilePath
        };

        await _transferUtility.DownloadAsync(request, ct);

        logger.LogInformation("Download complete: {Bytes:N0} bytes", new FileInfo(destinationFilePath).Length);
    }

    public async Task UploadDirectoryAsync(string localDirectory, string destinationPrefix, CancellationToken ct)
    {
        var bucket = options.Value.S3.OutputBucket;
        var prefix = destinationPrefix?.Trim('/') ?? string.Empty;

        if (!Directory.Exists(localDirectory))
        {
            logger.LogWarning("Upload directory does not exist: {LocalDir}", localDirectory);
            return;
        }

        var files = Directory.GetFiles(localDirectory, "*", SearchOption.AllDirectories);

        logger.LogInformation("Scanning {LocalDir} for upload to s3://{Bucket}/{Prefix}. Found {FileCount} files:",
            localDirectory, bucket, prefix, files.Length);

        long totalBytes = 0;
        foreach (var file in files)
        {
            var fileInfo = new FileInfo(file);
            var relativePath = Path.GetRelativePath(localDirectory, file).Replace('\\', '/');
            totalBytes += fileInfo.Length;
            logger.LogDebug("  [Discovered] {RelativePath} ({Bytes:N0} bytes)", relativePath, fileInfo.Length);
        }

        logger.LogInformation("Total output size: {TotalBytes:N0} bytes across {FileCount} files", totalBytes, files.Length);

        var completedCount = 0;

        await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = 10, CancellationToken = ct }, async (file, token) =>
        {
            var relativePath = Path.GetRelativePath(localDirectory, file).Replace('\\', '/');
            var key = string.IsNullOrEmpty(prefix) ? relativePath : $"{prefix}/{relativePath}";
            var fileInfo = new FileInfo(file);

            logger.LogDebug("[Upload Starting] {RelativePath} ({Bytes:N0} bytes) → s3://{Bucket}/{Key}",
                relativePath, fileInfo.Length, bucket, key);

            try
            {
                var request = new TransferUtilityUploadRequest
                {
                    FilePath = file,
                    BucketName = bucket,
                    Key = key
                };

                await _transferUtility.UploadAsync(request, token);

                var done = Interlocked.Increment(ref completedCount);
                logger.LogDebug("[Upload Complete] ({Done}/{Total}) {RelativePath} → s3://{Bucket}/{Key}",
                    done, files.Length, relativePath, bucket, key);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Upload Failed] {RelativePath} → s3://{Bucket}/{Key}: {Message}",
                    relativePath, bucket, key, ex.Message);
                throw;
            }
        });

        logger.LogInformation("Upload complete: {FileCount} files to s3://{Bucket}/{Prefix}",
            files.Length, bucket, prefix);
    }
}