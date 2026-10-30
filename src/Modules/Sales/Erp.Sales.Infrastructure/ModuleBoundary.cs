using Erp.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Sales.Infrastructure;

public static class Registration
{
    public static IServiceCollection AddSalesFoundation(this IServiceCollection services)
    {
        services.AddSingleton(new ModuleDescriptor("Sales", "sales"));
        return services;
    }
}
