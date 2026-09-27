using System.Collections.Concurrent;
using App.Interfaces;
using App.Models;
using FFMpegCore;
using FFMpegCore.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace App.Pipeline;

public class FFmpegTranscoder : ITranscoder
{
    private readonly IOptions<TranscoderOptions> _options;
    private readonly ILogger<FFmpegTranscoder> _logger;

    public FFmpegTranscoder(IOptions<TranscoderOptions> options, ILogger<FFmpegTranscoder> logger)
    {
        _options = options;
        _logger = logger;

        var ffmpegPath = options.Value.FFmpegBinaryPath;
        if (!string.IsNullOrEmpty(ffmpegPath))
        {
            GlobalFFOptions.Configure(new FFOptions { BinaryFolder = Path.GetDirectoryName(ffmpegPath) ?? string.Empty });
        }
    }

    public async Task EncodeAsync(string inputFile, string outputDirectory, IReadOnlyCollection<Rendition> renditions, CancellationToken ct)
    {
        Directory.CreateDirectory(outputDirectory);
        var maxEncoders = _options.Value.MaxEncoders;

        await Parallel.ForEachAsync(renditions, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = maxEncoders }, async (r, token) =>
        {
            var customArgs = $"-map 0:v:0 -map 0:a:0? -c:v libx264 -preset medium -crf {r.Crf} -maxrate {r.MaxBitrate}k -bufsize {r.MaxBitrate * 2}k -vf scale=w={r.Width}:h={r.Height}:force_original_aspect_ratio=decrease,pad=ceil(iw/2)*2:ceil(ih/2)*2 -pix_fmt yuv420p -c:a aac -b:a 128k -ar 44100";

            await FFMpegArguments
                .FromFileInput(inputFile, verifyExists: false)
                .OutputToFile(r.OutFile, overwrite: true, opt => opt
                    .WithCustomArgument(customArgs))
                .CancellableThrough(token)
                .ProcessAsynchronously(throwOnError: true);
        });
    }
}