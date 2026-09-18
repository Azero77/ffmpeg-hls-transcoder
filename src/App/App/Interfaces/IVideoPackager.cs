namespace App.Interfaces;

public interface IVideoPackager
{
    Task Package(string inputDir, string outDir, CancellationToken ct);
}