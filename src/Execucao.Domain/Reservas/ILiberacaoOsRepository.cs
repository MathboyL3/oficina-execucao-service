namespace Execucao.Domain.Reservas;

public interface ILiberacaoOsRepository
{
    Task<bool> ExisteAsync(Guid osId, CancellationToken ct = default);
    Task AdicionarAsync(LiberacaoOs liberacao, CancellationToken ct = default);
}
