using App.Interfaces;
using App.Models;
using CliWrap;
using CliWrap.Builders;
using Microsoft.Extensions.Options;

namespace App.Pipeline;

public class FFmpegTranscoder(IOptions<TranscoderOptions> options) : ITranscoder
{
    public async Task EncodeAsync(string inputFile, string outputDirectory, IReadOnlyCollection<Rendition> renditions, CancellationToken ct)
    {
        //input is the full path of source.mp4
        //output is the full path of Intermediates/{then 1080-720-360.....} with just encoded files without segments, then shaka packager will handle segmentation
        Directory.CreateDirectory(outputDirectory);
        var maxEncoders = options.Value.MaxEncoders;
        await Parallel.ForEachAsync(renditions, new ParallelOptions() { CancellationToken = ct , MaxDegreeOfParallelism = maxEncoders},async (r, token) =>
        {
            await Cli.Wrap(options.Value.FFmpegBinaryPath)
                .WithArguments((Action<ArgumentsBuilder>)GenerateArgs)
                .ExecuteAsync(token);
            return;

            void GenerateArgs(ArgumentsBuilder args) =>
                args.Add("-nostdin")
                    .Add("-hide_banner")
                    .Add("-y")
                    .Add("-i")
                    .Add(inputFile)
                    .Add("-map")
                    .Add("0:v:0")
                    .Add("-map")
                    .Add("0:a:0?")
                    .Add("-c:v")
                    .Add("libx264")
                    .Add("-preset")
                    .Add("medium")
                    .Add("-crf")
                    .Add(r.Crf.ToString())
                    .Add("-maxrate")
                    .Add($"{r.MaxBitrate}k")
                    .Add("-bufsize")
                    .Add($"{r.MaxBitrate * 2}k")
                    .Add("-vf")
                    .Add($"scale=w={r.Width}:h={r.Height}:force_original_aspect_ratio=decrease")
                    .Add("-pix_fmt")
                    .Add("yuv420p")
                    .Add("-c:a")
                    .Add("aac")
                    .Add("-b:a")
                    .Add("128k")
                    .Add("-ar")
                    .Add("44100")
                    .Add(r.OutFile);
        });
    }
}