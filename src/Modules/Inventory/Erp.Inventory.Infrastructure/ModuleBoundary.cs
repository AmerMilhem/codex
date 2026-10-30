using Erp.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Inventory.Infrastructure;

public static class Registration
{
    public static IServiceCollection AddInventoryFoundation(this IServiceCollection services)
    {
        services.AddSingleton(new ModuleDescriptor("Inventory", "inventory"));
        return services;
    }
}
