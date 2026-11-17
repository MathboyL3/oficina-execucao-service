using Execucao.Domain.Reservas;
using Microsoft.EntityFrameworkCore;

namespace Execucao.Infrastructure.Persistence.Repositories;

public sealed class ReservaRepository : IReservaPecaRepository
{
    private readonly ExecucaoDbContext _db;
    public ReservaRepository(ExecucaoDbContext db) => _db = db;

    public async Task<IReadOnlyList<ReservaPeca>> ListarPorOsAsync(Guid osId, CancellationToken ct = default)
        => await _db.Reservas.Where(r => r.OrdemServicoId == osId).ToListAsync(ct);

    public async Task AdicionarAsync(ReservaPeca reserva, CancellationToken ct = default)
        => await _db.Reservas.AddAsync(reserva, ct);
}

public sealed class LiberacaoOsRepository : ILiberacaoOsRepository
{
    private readonly ExecucaoDbContext _db;
    public LiberacaoOsRepository(ExecucaoDbContext db) => _db = db;

    public Task<bool> ExisteAsync(Guid osId, CancellationToken ct = default)
        => _db.Liberacoes.AnyAsync(l => l.OrdemServicoId == osId, ct);

    public async Task AdicionarAsync(LiberacaoOs liberacao, CancellationToken ct = default)
        => await _db.Liberacoes.AddAsync(liberacao, ct);
}
