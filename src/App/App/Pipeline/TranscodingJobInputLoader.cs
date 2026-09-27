using System.Text.Json;
using Amazon.S3;
using App.Interfaces;
using App.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace App.Pipeline;

/// <summary>
/// Loads job input from stdin (Fargate/SFN), Amazon S3 (s3://bucket/key or relative key), or a local JSON file
/// (set TRANSCODER__INPUT_FILE env var or --input-file CLI arg).
/// </summary>
public sealed class TranscodingJobInputLoader(
    ILogger<TranscodingJobInputLoader> logger,
    IServiceProvider serviceProvider,
    IOptions<TranscoderOptions>? options = null) : ITranscodingJobInputLoader
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
            var s3Client = serviceProvider.GetService(typeof(IAmazonS3)) as IAmazonS3;

            if (inputPath.StartsWith("s3://", StringComparison.OrdinalIgnoreCase))
            {
                if (s3Client is null)
                {
                    throw new InvalidOperationException(
                        $"Cannot load job input from '{inputPath}' because S3 storage provider is not configured.");
                }

                var uri = new Uri(inputPath);
                var bucket = uri.Host;
                var key = uri.AbsolutePath.TrimStart('/');

                logger.LogInformation("Downloading job input from s3://{Bucket}/{Key}", bucket, key);
                using var response = await s3Client.GetObjectAsync(bucket, key, ct);
                using var reader = new StreamReader(response.ResponseStream);
                json = await reader.ReadToEndAsync(ct);
            }
            else if (!File.Exists(inputPath) && s3Client is not null && !string.IsNullOrWhiteSpace(options?.Value.S3.InputBucket))
            {
                var bucket = options.Value.S3.InputBucket;
                var key = inputPath.TrimStart('/');

                logger.LogInformation("Downloading job input from s3://{Bucket}/{Key} (InputBucket)", bucket, key);
                using var response = await s3Client.GetObjectAsync(bucket, key, ct);
                using var reader = new StreamReader(response.ResponseStream);
                json = await reader.ReadToEndAsync(ct);
            }
            else
            {
                logger.LogInformation("Loading job input from file: {Path}", inputPath);
                json = await File.ReadAllTextAsync(inputPath, ct);
            }
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
