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
        var fileCount = Directory.GetFiles(localDirectory, "*", SearchOption.AllDirectories).Length;
        logger.LogInformation("Uploading {FileCount} files → s3://{Bucket}/{Prefix}",
            fileCount, bucket, destinationPrefix);

        var request = new TransferUtilityUploadDirectoryRequest
        {
            Directory = localDirectory,
            BucketName = bucket,
            KeyPrefix = destinationPrefix,
            UploadFilesConcurrently = true
        };

        await _transferUtility.UploadDirectoryAsync(request, ct);

        logger.LogInformation("Upload complete: {FileCount} files to s3://{Bucket}/{Prefix}",
            fileCount, bucket, destinationPrefix);
    }
}