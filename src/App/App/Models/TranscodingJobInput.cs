namespace App.Models;

public record TranscodingJobInput(
    Guid VideoId,
    Guid TenantId,
    S3InputLocation InputFile,
    S3OutputLocation OutputOptions,
    EncryptionMethod EncryptionMethod,
    VideoMetadatInput  VideoMetadatInput
    );

public record S3InputLocation(
    string BucketName,
    string Key);

public record S3OutputLocation(
    string BucketName,
    string OutputPrefix
);

public enum EncryptionMethod
{
    ClearKey,
    None
}

public record VideoMetadatInput(
    int SourceWidth,
    int SourceHeight,
    TimeSpan Duration
    ); 