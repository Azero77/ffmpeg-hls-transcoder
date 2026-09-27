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

        if (!Directory.Exists(localDirectory))
        {
            logger.LogWarning("Upload directory does not exist: {LocalDir}", localDirectory);
            return Task.CompletedTask;
        }

        Directory.CreateDirectory(destinationPrefix);

        var files = Directory.GetFiles(localDirectory, "*", SearchOption.AllDirectories);
        logger.LogInformation("Scanning {LocalDir} for local copy to {Dest}. Found {FileCount} files:",
            localDirectory, destinationPrefix, files.Length);

        long totalBytes = 0;
        foreach (var file in files)
        {
            var fileInfo = new FileInfo(file);
            var relativePath = Path.GetRelativePath(localDirectory, file);
            totalBytes += fileInfo.Length;
            logger.LogDebug("  [Discovered] {RelativePath} ({Bytes:N0} bytes)", relativePath, fileInfo.Length);
        }

        logger.LogInformation("Total output size: {TotalBytes:N0} bytes across {FileCount} files", totalBytes, files.Length);

        var completedCount = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(localDirectory, file);
            var destPath = Path.Combine(destinationPrefix, relativePath);
            var fileInfo = new FileInfo(file);

            var fileDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(fileDir))
                Directory.CreateDirectory(fileDir);

            logger.LogDebug("[Local Copy Starting] {RelativePath} ({Bytes:N0} bytes) → {DestPath}",
                relativePath, fileInfo.Length, destPath);

            File.Copy(file, destPath, overwrite: true);

            completedCount++;
            logger.LogDebug("[Local Copy Complete] ({Done}/{Total}) {RelativePath} → {DestPath}",
                completedCount, files.Length, relativePath, destPath);
        }

        logger.LogInformation("Local copy complete: {FileCount} files to {Dest}", files.Length, destinationPrefix);
        return Task.CompletedTask;
    }
}
