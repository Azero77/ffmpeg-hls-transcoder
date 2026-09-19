using App.Models;

namespace App.Interfaces;

public interface ITranscodingPipeline
{
    Task<ExitReason> ExecuteAsync(TranscodingJobInput input, CancellationToken ct);
}