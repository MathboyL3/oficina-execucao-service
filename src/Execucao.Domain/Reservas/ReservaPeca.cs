using Execucao.Domain.Common;

namespace Execucao.Domain.Reservas;

/// <summary>Rastro da reserva de uma peça para uma OS. O saldo fica em EstoquePeca.</summary>
public sealed class ReservaPeca : AggregateRoot
{
    public Guid OrdemServicoId { get; private set; }
    public Guid PecaId { get; private set; }
    public int Quantidade { get; private set; }
    public StatusReserva Status { get; private set; }
    public DateTime CriadaEm { get; private set; }

    private ReservaPeca() { }

    public static ReservaPeca Criar(Guid ordemServicoId, Guid pecaId, int quantidade)
    {
        if (ordemServicoId == Guid.Empty) throw new DomainException("OS é obrigatória.");
        if (pecaId == Guid.Empty) throw new DomainException("Peça é obrigatória.");
        if (quantidade <= 0) throw new DomainException("Quantidade deve ser positiva.");
        return new ReservaPeca
        {
            OrdemServicoId = ordemServicoId,
            PecaId = pecaId,
            Quantidade = quantidade,
            Status = StatusReserva.Ativa,
            CriadaEm = DateTime.UtcNow
        };
    }

    public void Consumir() => Mudar(StatusReserva.Consumida);
    public void Liberar() => Mudar(StatusReserva.Liberada);

    private void Mudar(StatusReserva para)
    {
        if (Status != StatusReserva.Ativa)
            throw new DomainException($"Reserva no status {Status} não pode ir para {para}.");
        Status = para;
    }
}
