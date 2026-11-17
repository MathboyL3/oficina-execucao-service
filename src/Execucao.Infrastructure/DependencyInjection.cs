using Execucao.Domain.Catalogo;
using Execucao.Domain.Common;
using Execucao.Domain.Estoque;
using Execucao.Domain.Execucoes;
using Execucao.Domain.Reservas;
using Execucao.Infrastructure.Auth;
using Execucao.Infrastructure.Persistence;
using Execucao.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Execucao.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration cfg)
    {
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IServicoRepository, ServicoRepository>();
        services.AddScoped<IPecaRepository, PecaRepository>();
        services.AddScoped<IEstoquePecaRepository, EstoquePecaRepository>();
        services.AddScoped<IReservaPecaRepository, ReservaRepository>();
        services.AddScoped<ILiberacaoOsRepository, LiberacaoOsRepository>();
        services.AddScoped<IExecucaoRepository, ExecucaoRepository>();

        services.Configure<JwtOptions>(cfg.GetSection("Jwt"));

        return services;
    }
}
