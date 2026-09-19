using App.Extensions;
using App.Interfaces;
using App.Models;
using App.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace App;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Configuration.AddEnvironmentVariables("TRANSCODER__");
        builder.Services.AddTranscoderServices(builder.Configuration);

        // Structured console logging — CloudWatch-friendly single-line format
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
            o.IncludeScopes = true;
        });

        var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

        ExitReason exitReason;

        try
        {
            var loader = host.Services.GetRequiredService<ITranscodingJobInputLoader>();
            var pipeline = host.Services.GetRequiredService<ITranscodingPipeline>();

            var job = await loader.LoadAsync(lifetime.ApplicationStopping);
            exitReason = await pipeline.ExecuteAsync(job, lifetime.ApplicationStopping);
        }
        catch (TranscodingInputValidationException ex)
        {
            exitReason = ExitReason.InvalidInput(ex.Message);
        }
        catch (System.Text.Json.JsonException ex)
        {
            exitReason = ExitReason.InvalidInput($"Invalid or malformed JSON input: {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            exitReason = ExitReason.Cancelled("Startup");
        }
        catch (Exception ex)
        {
            exitReason = ExitReason.Failure("Startup", ex);
        }
        finally
        {
            await host.StopAsync();
        }

        // Structured exit log — queryable in CloudWatch Insights
        if (exitReason.Code == ExitCode.Success)
        {
            logger.LogInformation(
                "Exiting: Code={ExitCode}, Stage={Stage}, Message={Message}",
                (int)exitReason.Code, exitReason.Stage, exitReason.Message);
        }
        else
        {
            logger.LogError(exitReason.Exception,
                "Exiting: Code={ExitCode}, Stage={Stage}, Message={Message}",
                (int)exitReason.Code, exitReason.Stage, exitReason.Message);
        }

        return (int)exitReason.Code;
    }
}