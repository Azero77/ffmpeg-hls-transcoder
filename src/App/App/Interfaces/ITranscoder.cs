using App.Models;

namespace App.Interfaces;

public interface ITranscoder
{
    Task RunAsync(string input, string outputDir, IReadOnlyCollection<Rendition> Renditions, CancellationToken ct);
}

public interface ITranscodingJobInputLoader
{
    Task<TranscodingJobInput> LoadAsync(CancellationToken ct);
}