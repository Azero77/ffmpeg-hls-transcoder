using App.Extensions;
using App.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace App;

class Program
{
    static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTranscoderServices();
        var host = builder.Build();
        var hostToken = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;

        try
        {
            var pipeline = host.Services.GetRequiredService<ITranscodingPipeline>();
            var loader =  host.Services.GetRequiredService<ITranscodingJobInputLoader>();
            var job = await loader.LoadAsync(hostToken);
            await pipeline.ExecuteAsync(job,hostToken);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            Environment.Exit(1);
            throw;
        }
    }
}