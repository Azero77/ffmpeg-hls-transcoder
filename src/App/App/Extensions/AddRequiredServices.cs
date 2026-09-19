using Amazon.S3;
using App.Interfaces;
using App.Models;
using App.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace App.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTranscoderServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind options: support both Transcoder section and stripped TRANSCODER__ prefix
        services.Configure<TranscoderOptions>(opts =>
        {
            configuration.GetSection("Transcoder").Bind(opts);
            configuration.Bind(opts);
        });

        var options = new TranscoderOptions();
        configuration.GetSection("Transcoder").Bind(options);
        configuration.Bind(options);

        // Transfer service — selected by StorageProvider
        switch (options.StorageProvider)
        {
            case StorageProvider.S3:
                services.AddAWSService<IAmazonS3>();
                services.AddSingleton<ITransferService, S3TransferService>();
                break;

            case StorageProvider.Local:
                services.AddSingleton<ITransferService, LocalTransferService>();
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported StorageProvider: '{options.StorageProvider}'. " +
                    "Supported values: S3, Local.");
        }

        // Pipeline services
        services.AddSingleton<ITranscoder, FFmpegTranscoder>();
        services.AddSingleton<IPackager, ShakaPackager>();
        services.AddSingleton<IThumbnailGenerator, FFmpegThumbnailGenerator>();
        services.AddSingleton<ITranscodingJobInputLoader, TranscodingJobInputLoader>();
        services.AddSingleton<ITranscodingPipeline, TranscodingPipeline>();

        return services;
    }
}