namespace App.Interfaces;

/// <summary>
/// Abstracts file transfer (download source, upload output).
/// Implementation selected by <see cref="Models.StorageProvider"/> at startup.
/// </summary>
public interface ITransferService
{
    /// <summary>Download the source video to a local path.</summary>
    /// <param name="sourcePath">S3 key or local file path, depending on provider.</param>
    /// <param name="destinationFilePath">Local filesystem path to write to.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DownloadAsync(string sourcePath, string destinationFilePath, CancellationToken ct);

    /// <summary>Upload an entire directory of output files.</summary>
    /// <param name="localDirectory">Local directory containing packaged output.</param>
    /// <param name="destinationPrefix">S3 prefix or local directory, depending on provider.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UploadDirectoryAsync(string localDirectory, string destinationPrefix, CancellationToken ct);

    /// <summary>Find the exact path/key of a file given a prefix (useful for unknown extensions).</summary>
    Task<string?> FindFileByPrefixAsync(string prefix, CancellationToken ct);
}