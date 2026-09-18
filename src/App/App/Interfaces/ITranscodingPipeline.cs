using App.Models;

namespace App.Interfaces;

public interface ITranscodingPipeline
{
    Task ExecuteAsync(TranscodingJobInput input, CancellationToken ct);
}