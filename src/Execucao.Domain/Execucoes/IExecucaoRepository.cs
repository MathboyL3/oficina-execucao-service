namespace Execucao.Domain.Execucoes;

public interface IExecucaoRepository
{
    Task<Execucao?> ObterPorIdAsync(Guid id, CancellationToken ct = default);
    Task<Execucao?> ObterPorOsAsync(Guid osId, CancellationToken ct = default);
    Task<IReadOnlyList<Execucao>> ListarFilaAsync(int skip, int take, CancellationToken ct = default);
    Task AdicionarAsync(Execucao execucao, CancellationToken ct = default);
}
