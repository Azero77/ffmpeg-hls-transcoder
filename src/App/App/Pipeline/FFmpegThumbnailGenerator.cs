using App.Interfaces;
using App.Models;
using CliWrap;
using CliWrap.Builders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace App.Pipeline;

/// <summary>
/// Generates a single poster-frame JPEG thumbnail using FFmpeg.
/// Uses keyframe-seek (-ss before -i) for speed.
/// </summary>
public sealed class FFmpegThumbnailGenerator(
    IOptions<TranscoderOptions> options,
    ILogger<FFmpegThumbnailGenerator> logger) : IThumbnailGenerator
{
    public async Task GenerateAsync(string sourceFile, string outputFile, CancellationToken ct)
    {
        var seekSeconds = options.Value.ThumbnailSeekSeconds;

        logger.LogInformation("Generating thumbnail at {SeekSeconds}s from {Source}",
            seekSeconds, sourceFile);

        var outputDir = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        await Cli.Wrap(options.Value.FFmpegBinaryPath)
            .WithArguments((Action<ArgumentsBuilder>)BuildArgs)
            .ExecuteAsync(ct);

        logger.LogInformation("Thumbnail generated: {OutputFile} ({Bytes:N0} bytes)",
            outputFile, new FileInfo(outputFile).Length);

        return;

        void BuildArgs(ArgumentsBuilder args) =>
            args.Add("-nostdin")
                .Add("-hide_banner")
                .Add("-y")
                // -ss before -i = keyframe seek (fast)
                .Add("-ss").Add(seekSeconds.ToString())
                .Add("-i").Add(sourceFile)
                .Add("-frames:v").Add("1")
                .Add("-q:v").Add("2")
                .Add(outputFile);
    }
}
