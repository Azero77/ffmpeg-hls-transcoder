using Amazon.S3;
using Amazon.S3.Transfer;
using App.Interfaces;
using App.Models;

namespace App.Pipeline;

public class S3TransferService(IAmazonS3 s3Client) : ITransferService
{
    private readonly TransferUtility _transferUtility = new(s3Client, new TransferUtilityConfig()
    {
        ConcurrentServiceRequests = 8
    });
    public async Task DownloadInput(S3InputLocation input, string filePath, CancellationToken ct)
    {
        var downloadRequest = new TransferUtilityDownloadRequest()
        {
            BucketName = input.BucketName,
            Key = input.Key,
            FilePath = filePath
        };
        
        await _transferUtility.DownloadAsync(downloadRequest, ct);
    }

    public async Task UploadOuput(S3OutputLocation output, string keyPrefix,string outputDir, CancellationToken ct)
    {
        var request = new TransferUtilityUploadDirectoryRequest()
        {
            Directory = outputDir,
            BucketName = output.BucketName,
            KeyPrefix = keyPrefix,
            UploadFilesConcurrently = true,
            
        };
        await _transferUtility.UploadDirectoryAsync(request, ct);
    }
}