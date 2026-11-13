using Execucao.Domain.Common;

namespace Execucao.Infrastructure.Persistence;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly ExecucaoDbContext _ctx;
    public UnitOfWork(ExecucaoDbContext ctx) => _ctx = ctx;
    public Task<int> CommitAsync(CancellationToken ct = default) => _ctx.SaveChangesAsync(ct);
}
