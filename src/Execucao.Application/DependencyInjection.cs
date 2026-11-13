using Execucao.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace Execucao.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ServicoUseCases>();
        services.AddScoped<PecaUseCases>();
        services.AddScoped<ExecucaoUseCases>();
        return services;
    }
}
