using Erp.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Procurement.Infrastructure;

public static class Registration
{
    public static IServiceCollection AddProcurementFoundation(this IServiceCollection services)
    {
        services.AddSingleton(new ModuleDescriptor("Procurement", "procurement"));
        return services;
    }
}
