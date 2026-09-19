using App.Interfaces;
using Microsoft.Extensions.Logging;

namespace App.Pipeline;

/// <summary>
/// Local filesystem transfer service for development and testing.
/// SourcePath = absolute file path; DestinationPrefix = absolute directory path.
/// </summary>
public sealed class LocalTransferService(ILogger<LocalTransferService> logger) : ITransferService
{
    public Task DownloadAsync(string sourcePath, string destinationFilePath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        logger.LogInformation("Local copy: {Source} → {Dest}", sourcePath, destinationFilePath);

        var destDir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);

        File.Copy(sourcePath, destinationFilePath, overwrite: true);

        logger.LogInformation("Local copy complete: {Bytes:N0} bytes", new FileInfo(destinationFilePath).Length);
        return Task.CompletedTask;
    }

    public Task UploadDirectoryAsync(string localDirectory, string destinationPrefix, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        logger.LogInformation("Local copy directory: {Source} → {Dest}", localDirectory, destinationPrefix);
        Directory.CreateDirectory(destinationPrefix);

        foreach (var file in Directory.GetFiles(localDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(localDirectory, file);
            var destPath = Path.Combine(destinationPrefix, relativePath);

            var fileDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(fileDir))
                Directory.CreateDirectory(fileDir);

            File.Copy(file, destPath, overwrite: true);
        }

        var fileCount = Directory.GetFiles(localDirectory, "*", SearchOption.AllDirectories).Length;
        logger.LogInformation("Local copy complete: {FileCount} files to {Dest}", fileCount, destinationPrefix);
        return Task.CompletedTask;
    }
}
