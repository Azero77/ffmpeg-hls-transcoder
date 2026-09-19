namespace App.Models;

public class Workspace
{
    
    public static string WorkspacePath = Path.Combine(Directory.GetCurrentDirectory(), "workspace");
    public string VideoId { get; private set; }
    public Workspace(Guid videoId)
    {
        VideoId = videoId.ToString();
    }

    public void Create()
    {
        Directory.CreateDirectory(CurrentDir);
    }

    public string CurrentDir => Path.Combine(WorkspacePath, VideoId);
    public string SourceFile => Path.Combine(CurrentDir, "source.mp4");
    public string IntermediatesDirectory => Path.Combine(CurrentDir, "intermediates");
    public string OutputDirectory => Path.Combine(CurrentDir, "output");

}