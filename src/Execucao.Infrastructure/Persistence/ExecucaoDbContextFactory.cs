using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Execucao.Infrastructure.Persistence;

/// <summary>
/// Permite ao EF Core CLI criar o DbContext em design-time (migrations) sem precisar
/// de Postgres rodando ou de configuração externa.
/// </summary>
public class ExecucaoDbContextFactory : IDesignTimeDbContextFactory<ExecucaoDbContext>
{
    public ExecucaoDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ExecucaoDbContext>()
            .UseNpgsql("Host=localhost;Database=oficina_design;Username=design;Password=design")
            .Options;
        return new ExecucaoDbContext(options);
    }
}
