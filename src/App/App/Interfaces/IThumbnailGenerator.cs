namespace App.Interfaces;

/// <summary>
/// Generates a poster-frame thumbnail from a source video.
/// </summary>
public interface IThumbnailGenerator
{
    /// <summary>
    /// Generate a JPEG thumbnail at the 10% seek position.
    /// </summary>
    /// <param name="sourceFile">Absolute path to the source video.</param>
    /// <param name="outputFile">Absolute path for the output JPEG.</param>
    /// <param name="duration">Duration of the source video.</param>
    Task GenerateAsync(string sourceFile, string outputFile, TimeSpan duration, CancellationToken ct);
}
