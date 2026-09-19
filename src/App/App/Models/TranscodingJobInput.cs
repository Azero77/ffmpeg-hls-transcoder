using System.Text.Json.Serialization;

namespace App.Models;

/// <summary>
/// Root job input deserialized from SFN JSON payload or local file.
/// Immutable record — validated at load time by <see cref="TranscodingJobInputValidator"/>.
/// </summary>
public sealed record TranscodingJobInput
{
    public required Guid VideoId { get; init; }
    public required Guid TenantId { get; init; }

    /// <summary>
    /// Source location — S3 key (e.g. "tenantId/videoId/source.mp4") for S3 provider,
    /// or absolute local path for Local provider.
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// Output prefix — S3 key prefix or local directory, depending on StorageProvider.
    /// </summary>
    public required string OutputPrefix { get; init; }

    public required VideoMetadata SourceMetadata { get; init; }
    public required TranscodeSettings Settings { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<EncryptionMethod>))]
    public EncryptionMethod EncryptionMethod { get; init; } = EncryptionMethod.None;

    /// <summary>
    /// Raw encryption keys. Safe to pass as plaintext — SFN runs in a private VPC.
    /// Required when EncryptionMethod != None.
    /// </summary>
    public EncryptionSettings? Encryption { get; init; }

    /// <summary>
    /// If set, thumbnail generation is skipped (thumbnail already exists upstream).
    /// If null/empty, the pipeline generates a poster frame at ~5s.
    /// </summary>
    public string? ThumbnailRelativeUrl { get; init; }
}

public sealed record VideoMetadata(
    int SourceWidth,
    int SourceHeight,
    TimeSpan Duration);

public sealed record TranscodeSettings
{
    public required OutputPreset[] Outputs { get; init; }
    public int SegmentLengthSeconds { get; init; } = 6;
    public int FragmentLengthSeconds { get; init; } = 2;
    public AudioSettings Audio { get; init; } = AudioSettings.Default;
}

public sealed record OutputPreset(
    int Width,
    int Height,
    int MaxBitrateKbps,
    int QvbrQualityLevel,
    string NameModifier);

public sealed record AudioSettings(
    string Codec,
    int BitrateKbps,
    int SampleRate)
{
    public static readonly AudioSettings Default = new("aac", 128, 44100);
}

/// <summary>
/// Raw-key CENC encryption parameters. Passed as plaintext in the job JSON
/// (the SFN task runs inside a private VPC with no public ingress).
/// </summary>
public sealed record EncryptionSettings(
    string KeyId,
    string Key,
    string? KeyUrl);

[JsonConverter(typeof(JsonStringEnumConverter<EncryptionMethod>))]
public enum EncryptionMethod
{
    None = 0,
    ClearKey = 1
}