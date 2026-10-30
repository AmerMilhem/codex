using Erp.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Localization.Infrastructure;

public static class Registration
{
    public static IServiceCollection AddLocalizationFoundation(this IServiceCollection services)
    {
        services.AddSingleton(new ModuleDescriptor("Localization", "localization"));
        return services;
    }
}
