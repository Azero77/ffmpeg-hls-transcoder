using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace App.Extensions;

public static class AddRequiredServices
{
    public static IServiceCollection AddTranscoderServices(this IServiceCollection services)
    {   
        return services;
    }
}