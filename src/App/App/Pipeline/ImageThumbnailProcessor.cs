using AlphaZero.ImageProcessing;
using App.Interfaces;
using App.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace App.Pipeline;

public sealed class ImageThumbnailProcessor(
    IImageScaler scaler,
    IOptions<TranscoderOptions> options,
    ILogger<ImageThumbnailProcessor> logger) : IImageThumbnailProcessor
{
    public async Task ProcessAsync(string inputImagePath, string outputPath, CancellationToken ct)
    {
        var scaleOptions = new ImageScaleOptions
        {
            TargetWidth = options.Value.ThumbnailWidth,
            TargetHeight = options.Value.ThumbnailHeight,
            JpegQuality = options.Value.ThumbnailJpegQuality,
            Mode = ScaleMode.LetterboxPad,
            PadColor = "#000000"
        };

        logger.LogInformation("Processing thumbnail {Input} -> {Output} ({Width}x{Height} Q{Quality})",
            inputImagePath, outputPath, scaleOptions.TargetWidth, scaleOptions.TargetHeight, scaleOptions.JpegQuality);

        await scaler.ScaleAsync(inputImagePath, outputPath, scaleOptions, ct);

        logger.LogInformation("Thumbnail processing complete: {Output} ({Bytes:N0} bytes)",
            outputPath, new FileInfo(outputPath).Length);
    }
}
