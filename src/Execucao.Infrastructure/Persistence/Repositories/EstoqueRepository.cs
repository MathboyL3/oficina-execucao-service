using Execucao.Domain.Estoque;
using Microsoft.EntityFrameworkCore;

namespace Execucao.Infrastructure.Persistence.Repositories;

public sealed class EstoquePecaRepository : IEstoquePecaRepository
{
    private readonly ExecucaoDbContext _ctx;
    public EstoquePecaRepository(ExecucaoDbContext ctx) => _ctx = ctx;

    public Task<EstoquePeca?> ObterPorIdAsync(Guid id, CancellationToken ct = default) =>
        _ctx.Estoques.Include(e => e.Movimentacoes).FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<EstoquePeca?> ObterPorPecaIdAsync(Guid pecaId, CancellationToken ct = default) =>
        _ctx.Estoques.Include(e => e.Movimentacoes).FirstOrDefaultAsync(e => e.PecaId == pecaId, ct);

    public async Task<IReadOnlyList<EstoquePeca>> ListarBaixosAsync(CancellationToken ct = default) =>
        await _ctx.Estoques
            .Where(e => e.QuantidadeDisponivel + e.QuantidadeReservada <= e.LimiteMinimo)
            .ToListAsync(ct);

    public Task AdicionarAsync(EstoquePeca estoque, CancellationToken ct = default)
    {
        _ctx.Estoques.Add(estoque);
        return Task.CompletedTask;
    }
}
