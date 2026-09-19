using System.Text.Json.Serialization;

namespace App.Models;

/// <summary>
/// Root configuration bound from "Transcoder" section.
/// <code>
/// { "Transcoder": { "StorageProvider": "S3", "MaxEncoders": 4, ... } }
/// </code>
/// </summary>
public sealed record TranscoderOptions
{
    /// <summary>Which transfer backend to use.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<StorageProvider>))]
    public StorageProvider StorageProvider { get; init; } = StorageProvider.Local;

    /// <summary>Max parallel FFmpeg encode processes.</summary>
    public int MaxEncoders { get; init; } = 4;

    /// <summary>Path to the Shaka Packager binary.</summary>
    public string PackagerBinaryPath { get; init; } = "packager";

    /// <summary>Path to the FFmpeg binary.</summary>
    public string FFmpegBinaryPath { get; init; } = "ffmpeg";

    /// <summary>S3-specific options. Only required when StorageProvider = S3.</summary>
    public S3Options S3 { get; init; } = new();

    /// <summary>Seek position (seconds) for thumbnail generation.</summary>
    public int ThumbnailSeekSeconds { get; init; } = 5;
}

public sealed record S3Options
{
    public string InputBucket { get; init; } = string.Empty;
    public string OutputBucket { get; init; } = string.Empty;
}

/// <summary>
/// Storage backend selector. Extensible — add Azure, GCS, MinIO
/// by adding a new enum value + ITransferService implementation.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StorageProvider>))]
public enum StorageProvider
{
    /// <summary>Local filesystem (dev/test).</summary>
    Local = 0,

    /// <summary>Amazon S3 (production).</summary>
    S3 = 1
}
