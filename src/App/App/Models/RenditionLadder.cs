namespace App.Models;

/// <summary>
/// Filters output presets to source resolution:
/// drop presets wider than source, always keep at least the lowest.
/// Ported from MediaConvertTranscodingService.cs:111-125.
/// </summary>
public static class RenditionLadder
{
    /// <summary>
    /// CRF mapping: higher resolution → lower CRF (better quality at higher bitrate).
    /// </summary>
    private static byte CrfForHeight(int height) => height switch
    {
        >= 1080 => 18,
        >= 720 => 20,
        >= 480 => 22,
        _ => 23
    };

    public static IReadOnlyList<Rendition> Filter(
        OutputPreset[] presets,
        int sourceWidth,
        string intermediatesDir)
    {
        // Sort by width descending
        var sorted = presets.OrderByDescending(p => p.Width).ToList();

        // Keep presets whose width <= sourceWidth
        var filtered = sorted.Where(p => p.Width <= sourceWidth).ToList();

        // Always keep at least the lowest resolution preset
        if (filtered.Count == 0 && sorted.Count > 0)
            filtered.Add(sorted[^1]);

        return filtered.Select(p => new Rendition(
            Resolution: p.Height,
            Crf: CrfForHeight(p.Height),
            MaxBitrate: p.MaxBitrateKbps,
            Width: p.Width,
            Height: p.Height,
            NameModifier: p.NameModifier,
            OutFile: Path.Combine(intermediatesDir, $"{p.NameModifier.TrimStart('_')}.mp4")
        )).ToList();
    }
}
