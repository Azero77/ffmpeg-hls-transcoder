namespace App.Models;

/// <summary>
/// Manages the temporary workspace directory tree for a transcoding job.
/// Creates {BasePath}/{VideoId}/ with subdirectories.
/// Disposes by recursively deleting the workspace.
/// </summary>
public sealed class Workspace : IDisposable
{
    private static readonly string BasePath =
        Path.Combine(Path.GetTempPath(), "transcode");

    public string VideoId { get; }
    public string CurrentDir { get; }
    public string SourceFile => Path.Combine(CurrentDir, "source.mp4");
    public string IntermediatesDirectory => Path.Combine(CurrentDir, "intermediates");
    public string OutputDirectory => Path.Combine(CurrentDir, "output");
    public string ThumbnailFile => Path.Combine(OutputDirectory, "poster.jpg");

    public Workspace(Guid videoId)
    {
        VideoId = videoId.ToString();
        CurrentDir = Path.Combine(BasePath, VideoId);
    }

    public void Create()
    {
        Directory.CreateDirectory(CurrentDir);
        Directory.CreateDirectory(IntermediatesDirectory);
        Directory.CreateDirectory(OutputDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(CurrentDir))
                Directory.Delete(CurrentDir, recursive: true);
        }
        catch
        {
            // Best-effort — Fargate ephemeral storage is wiped on task stop regardless.
        }
    }
}