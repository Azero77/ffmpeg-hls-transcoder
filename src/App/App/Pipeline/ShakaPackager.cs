using System.Text;
using App.Interfaces;
using App.Models;
using CliWrap;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace App.Pipeline;

/// <summary>
/// Packages CMAF/fMP4 HLS using Shaka Packager CLI.
/// Generates per-rendition segments, init files, I-frame playlists,
/// and a master HLS playlist — CMAF parity with MediaConvert job.json output.
/// </summary>
public sealed class ShakaPackager(
    IOptions<TranscoderOptions> options,
    ILogger<ShakaPackager> logger) : IPackager
{
    public async Task PackageAsync(
        string intermediatesDir,
        string outputDir,
        IReadOnlyList<Rendition> renditions,
        TranscodeSettings settings,
        EncryptionSettings? encryption,
        CancellationToken ct)
    {
        Directory.CreateDirectory(outputDir);

        var firstRendition = renditions[0];
        var audioInputProbe = Path.Combine(intermediatesDir, $"{firstRendition.NameModifier.TrimStart('_')}.mp4");
        bool hasAudio = false;
        try
        {
            var probeOutput = new StringBuilder();
            await Cli.Wrap(options.Value.PackagerBinaryPath)
                .WithArguments($"in={audioInputProbe} --dump_stream_info")
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.ToStringBuilder(probeOutput))
                .WithStandardErrorPipe(PipeTarget.ToStringBuilder(probeOutput))
                .ExecuteAsync(ct);

            hasAudio = probeOutput.ToString().Contains("type: Audio", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to probe {AudioInput} for audio streams using packager. Assuming audio exists.", audioInputProbe);
            hasAudio = true;
        }

        var args = BuildArguments(intermediatesDir, outputDir, renditions, settings, encryption, hasAudio);

        logger.LogInformation("Shaka Packager starting with {RenditionCount} streams (HasAudio: {HasAudio})", renditions.Count, hasAudio);
        logger.LogDebug("Packager args (redacted): {Args}", RedactArgs(args));

        var stdErrBuffer = new StringBuilder();

        var result = await Cli.Wrap(options.Value.PackagerBinaryPath)
            .WithArguments(args)
            .WithValidation(CommandResultValidation.None)
            .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stdErrBuffer))
            .WithWorkingDirectory(outputDir)
            .ExecuteAsync(ct);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Shaka Packager failed with exit code {result.ExitCode}. Stderr: {stdErrBuffer}");
        }

        logger.LogInformation("Shaka Packager completed successfully");

        if (stdErrBuffer.Length > 0)
            logger.LogDebug("Packager stderr: {StdErr}", stdErrBuffer.ToString());
    }

    private static string[] BuildArguments(
        string intermediatesDir,
        string outputDir,
        IReadOnlyList<Rendition> renditions,
        TranscodeSettings settings,
        EncryptionSettings? encryption,
        bool hasAudio)
    {
        var args = new List<string>();

        // --- Video stream descriptors ---
        foreach (var r in renditions)
        {
            var label = r.Height >= 720 ? "HD" : "SD";
            var name = r.NameModifier.TrimStart('_');
            var inputFile = Path.Combine(intermediatesDir, $"{name}.mp4");

            args.Add(string.Join(",",
                $"in={inputFile}",
                "stream=video",
                $"init_segment={name}/init.mp4",
                $"segment_template={name}/$Number$.m4s",
                $"playlist_name={name}.m3u8",
                $"iframe_playlist_name={name}_iframe.m3u8",
                $"drm_label={label}"));
        }

        // --- Audio stream descriptor (from first rendition's file) ---
        if (hasAudio)
        {
            var firstRendition = renditions[0];
            var audioInput = Path.Combine(intermediatesDir,
                $"{firstRendition.NameModifier.TrimStart('_')}.mp4");

            args.Add(string.Join(",",
                $"in={audioInput}",
                "stream=audio",
                "init_segment=audio/init.mp4",
                "segment_template=audio/$Number$.m4s",
                "playlist_name=audio.m3u8",
                "hls_group_id=audio",
                "hls_name=English",
                "drm_label=AUDIO"));
        }

        // --- HLS output flags ---
        args.AddRange([
            "--hls_master_playlist_output", "master.m3u8",
            "--hls_playlist_type", "VOD",
            "--segment_duration", settings.SegmentLengthSeconds.ToString(),
            "--fragment_duration", settings.FragmentLengthSeconds.ToString()
        ]);

        // --- Encryption (raw key ClearKey / SAMPLE-AES) ---
        if (encryption is not null)
        {
            args.Add("--enable_raw_key_encryption");
            args.Add("--protection_scheme");
            args.Add("cbcs");
            args.Add("--clear_lead");
            args.Add("0");
            args.Add("--keys");
            args.Add(string.Join(",",
                $"label=HD:key_id={encryption.KeyId}:key={encryption.Key}",
                $"label=SD:key_id={encryption.KeyId}:key={encryption.Key}",
                $"label=AUDIO:key_id={encryption.KeyId}:key={encryption.Key}"));

            if (!string.IsNullOrWhiteSpace(encryption.KeyUrl))
            {
                args.Add("--hls_key_uri");
                args.Add(encryption.KeyUrl);
            }
        }

        return [.. args];
    }

    /// <summary>Redact encryption keys from log output.</summary>
    private static string RedactArgs(string[] args) =>
        string.Join(" ", args.Select(a =>
            a.Contains("key=", StringComparison.OrdinalIgnoreCase) ||
            a.Contains("key_id=", StringComparison.OrdinalIgnoreCase)
                ? "[REDACTED]"
                : a));
}
