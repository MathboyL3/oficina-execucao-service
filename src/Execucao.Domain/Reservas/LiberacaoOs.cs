using Execucao.Domain.Common;

namespace Execucao.Domain.Reservas;

/// <summary>Marcador de que a OS foi liberada/cancelada. Impede reserva tardia de peças.</summary>
public sealed class LiberacaoOs : AggregateRoot
{
    public Guid OrdemServicoId { get; private set; }
    public DateTime LiberadaEm { get; private set; }

    private LiberacaoOs() { }

    public static LiberacaoOs Criar(Guid ordemServicoId)
    {
        if (ordemServicoId == Guid.Empty) throw new DomainException("OS é obrigatória.");
        return new LiberacaoOs
        {
            OrdemServicoId = ordemServicoId,
            LiberadaEm = DateTime.UtcNow
        };
    }
}
