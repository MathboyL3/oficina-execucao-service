using Execucao.Domain.Execucoes;
using Microsoft.EntityFrameworkCore;

namespace Execucao.Infrastructure.Persistence.Repositories;

public sealed class ExecucaoRepository : IExecucaoRepository
{
    private static readonly StatusExecucao[] Ativas = { StatusExecucao.NaFila, StatusExecucao.EmDiagnostico, StatusExecucao.EmReparo };
    private readonly ExecucaoDbContext _db;
    public ExecucaoRepository(ExecucaoDbContext db) => _db = db;

    public Task<Domain.Execucoes.Execucao?> ObterPorIdAsync(Guid id, CancellationToken ct = default)
        => _db.Execucoes.FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<Domain.Execucoes.Execucao?> ObterPorOsAsync(Guid osId, CancellationToken ct = default)
        => _db.Execucoes.FirstOrDefaultAsync(e => e.OrdemServicoId == osId, ct);

    public async Task<IReadOnlyList<Domain.Execucoes.Execucao>> ListarFilaAsync(int skip, int take, CancellationToken ct = default)
        => await _db.Execucoes.Where(e => Ativas.Contains(e.Status))
            .OrderByDescending(e => e.Prioridade).ThenBy(e => e.EnfileiradaEm)
            .Skip(skip).Take(take).ToListAsync(ct);

    public async Task AdicionarAsync(Domain.Execucoes.Execucao execucao, CancellationToken ct = default)
        => await _db.Execucoes.AddAsync(execucao, ct);
}
