namespace App.Interfaces;

public interface IImageThumbnailProcessor
{
    /// <summary>
    /// Processes a raw image (either frame-extracted or user-uploaded)
    /// into a standardized 1280x720 JPEG thumbnail.
    /// </summary>
    Task ProcessAsync(string inputImagePath, string outputPath, CancellationToken ct);
}
