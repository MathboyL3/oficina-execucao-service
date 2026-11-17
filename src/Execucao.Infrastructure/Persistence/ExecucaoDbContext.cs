using Execucao.Domain.Catalogo;
using Execucao.Domain.Estoque;
using Execucao.Domain.Reservas;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Execucao.Infrastructure.Persistence;

public class ExecucaoDbContext : DbContext
{
    public ExecucaoDbContext(DbContextOptions<ExecucaoDbContext> options) : base(options) { }

    public DbSet<Servico> Servicos => Set<Servico>();
    public DbSet<Peca> Pecas => Set<Peca>();
    public DbSet<EstoquePeca> Estoques => Set<EstoquePeca>();
    public DbSet<MovimentacaoEstoque> Movimentacoes => Set<MovimentacaoEstoque>();
    public DbSet<ReservaPeca> Reservas => Set<ReservaPeca>();
    public DbSet<LiberacaoOs> Liberacoes => Set<LiberacaoOs>();
    public DbSet<Domain.Execucoes.Execucao> Execucoes => Set<Domain.Execucoes.Execucao>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.ApplyConfigurationsFromAssembly(typeof(ExecucaoDbContext).Assembly);
        // Tabelas do outbox transacional / inbox (idempotência) do MassTransit.
        b.AddInboxStateEntity();
        b.AddOutboxMessageEntity();
        b.AddOutboxStateEntity();
    }
}
