using System.Text.Json;
using App.Interfaces;
using App.Models;
using Microsoft.Extensions.Logging;

namespace App.Pipeline;

/// <summary>
/// Loads job input from stdin (Fargate/SFN) or a local JSON file
/// (set TRANSCODER__INPUT_FILE env var or --input-file CLI arg).
/// </summary>
public sealed class TranscodingJobInputLoader(
    ILogger<TranscodingJobInputLoader> logger) : ITranscodingJobInputLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public async Task<TranscodingJobInput> LoadAsync(CancellationToken ct)
    {
        var inputPath = Environment.GetEnvironmentVariable("TRANSCODER__INPUT_FILE");
        string json;

        if (!string.IsNullOrWhiteSpace(inputPath))
        {
            logger.LogInformation("Loading job input from file: {Path}", inputPath);
            json = await File.ReadAllTextAsync(inputPath, ct);
        }
        else
        {
            logger.LogInformation("Loading job input from stdin");
            using var reader = new StreamReader(Console.OpenStandardInput());
            json = await reader.ReadToEndAsync(ct);
        }

        if (string.IsNullOrWhiteSpace(json))
            throw new TranscodingInputValidationException("Job input is empty.");

        var input = JsonSerializer.Deserialize(json, TranscodingJsonSerializerContext.Default.TranscodingJobInput)
            ?? throw new TranscodingInputValidationException("Failed to deserialize job input.");

        var errors = TranscodingJobInputValidator.Validate(input);
        if (errors.Count > 0)
        {
            var msg = string.Join("; ", errors);
            logger.LogError("Job input validation failed: {Errors}", msg);
            throw new TranscodingInputValidationException(msg);
        }

        // Log redacted summary (no encryption keys)
        logger.LogInformation(
            "Job loaded: VideoId={VideoId}, Tenant={TenantId}, Source={SourcePath}, " +
            "Resolution={Width}x{Height}, Renditions={Count}, Encryption={Encryption}, " +
            "ThumbnailProvided={HasThumb}",
            input.VideoId, input.TenantId, input.SourcePath,
            input.SourceMetadata.SourceWidth, input.SourceMetadata.SourceHeight,
            input.Settings.Outputs.Length, input.EncryptionMethod,
            !string.IsNullOrWhiteSpace(input.ThumbnailRelativeUrl));

        return input;
    }
}

public sealed class TranscodingInputValidationException(string message) : Exception(message);
