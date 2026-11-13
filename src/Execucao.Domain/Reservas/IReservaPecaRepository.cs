namespace Execucao.Domain.Reservas;

public interface IReservaPecaRepository
{
    Task<IReadOnlyList<ReservaPeca>> ListarPorOsAsync(Guid osId, CancellationToken ct = default);
    Task AdicionarAsync(ReservaPeca reserva, CancellationToken ct = default);
}
