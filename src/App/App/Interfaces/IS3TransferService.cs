using App.Models;

namespace App.Interfaces;

public interface IS3TransferService
{
    Task DownloadInput(S3InputLocation input,string dir,CancellationToken ct);
    Task UploadOuput(S3OutputLocation output, string outputDir,CancellationToken ct);
}