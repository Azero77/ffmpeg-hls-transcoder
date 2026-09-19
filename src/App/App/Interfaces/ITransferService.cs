using App.Models;

namespace App.Interfaces;

public interface ITransferService
{
    Task DownloadInput(S3InputLocation input,string filePath,CancellationToken ct);
    Task UploadOuput(S3OutputLocation output, string keyPrefix,string outputDir,CancellationToken ct);
}