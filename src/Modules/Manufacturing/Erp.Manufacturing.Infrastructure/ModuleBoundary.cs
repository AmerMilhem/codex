using Erp.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Manufacturing.Infrastructure;

public static class Registration
{
    public static IServiceCollection AddManufacturingFoundation(this IServiceCollection services)
    {
        services.AddSingleton(new ModuleDescriptor("Manufacturing", "manufacturing"));
        return services;
    }
}
