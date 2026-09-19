using App.Models;

namespace App.Interfaces;

public interface ITranscoder
{
    Task EncodeAsync(string input, string outputDir, IReadOnlyCollection<Rendition> renditions, CancellationToken ct);
}

public interface ITranscodingJobInputLoader
{
    Task<TranscodingJobInput> LoadAsync(CancellationToken ct);
}