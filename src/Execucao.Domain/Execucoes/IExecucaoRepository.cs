namespace Execucao.Domain.Execucoes;

public interface IExecucaoRepository
{
    Task<Execucao?> ObterPorIdAsync(Guid id, CancellationToken ct = default);
    Task<Execucao?> ObterPorOsAsync(Guid osId, CancellationToken ct = default);

    /// <summary>
    /// Fila de execução: apenas as execuções com status <see cref="StatusExecucao.NaFila"/>,
    /// <see cref="StatusExecucao.EmDiagnostico"/> ou <see cref="StatusExecucao.EmReparo"/>,
    /// ordenadas por <c>Prioridade</c> desc e, no empate, pela mais antiga (<c>EnfileiradaEm</c> asc).
    /// </summary>
    Task<IReadOnlyList<Execucao>> ListarFilaAsync(int skip, int take, CancellationToken ct = default);
    Task AdicionarAsync(Execucao execucao, CancellationToken ct = default);
}
