using App.Models;

namespace App.Interfaces;

/// <summary>
/// Packages encoded intermediates into CMAF/fMP4 HLS with optional encryption.
/// </summary>
public interface IPackager
{
    /// <summary>
    /// Package encoded video/audio files into HLS CMAF segments + master playlist.
    /// </summary>
    /// <param name="intermediatesDir">Directory containing encoded rendition .mp4 files.</param>
    /// <param name="outputDir">Directory to write packaged segments, init files, and playlists.</param>
    /// <param name="renditions">The renditions that were encoded (order matters for stream descriptors).</param>
    /// <param name="settings">Transcode settings (segment duration, fragment duration, audio).</param>
    /// <param name="encryption">Encryption settings; null when EncryptionMethod == None.</param>
    Task PackageAsync(
        string intermediatesDir,
        string outputDir,
        IReadOnlyList<Rendition> renditions,
        TranscodeSettings settings,
        EncryptionSettings? encryption,
        CancellationToken ct);
}
