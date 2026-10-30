using Erp.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Workflow.Infrastructure;

public static class Registration
{
    public static IServiceCollection AddWorkflowFoundation(this IServiceCollection services)
    {
        services.AddSingleton(new ModuleDescriptor("Workflow", "workflow"));
        return services;
    }
}
