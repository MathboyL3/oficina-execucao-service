using Execucao.Domain.Common;
using Execucao.Domain.Estoque;
using Execucao.Domain.Reservas;
using MassTransit;
using Oficina.Contracts;

namespace Execucao.Application.Consumers;

/// <summary>Reserva todas as peças da OS (tudo ou nada). Idempotente por OS.</summary>
public sealed class ReservarPecasConsumer : IConsumer<ReservarPecas>
{
    private readonly IEstoquePecaRepository _estoque;
    private readonly IReservaPecaRepository _reservas;
    private readonly IUnitOfWork _uow;

    public ReservarPecasConsumer(IEstoquePecaRepository estoque, IReservaPecaRepository reservas, IUnitOfWork uow)
    {
        _estoque = estoque;
        _reservas = reservas;
        _uow = uow;
    }

    public async Task Consume(ConsumeContext<ReservarPecas> context)
    {
        var msg = context.Message;
        var ct = context.CancellationToken;

        var existentes = await _reservas.ListarPorOsAsync(msg.OrdemServicoId, ct);
        if (existentes.Any(r => r.Status == StatusReserva.Ativa))
        {
            await context.Publish(new PecasReservadas(msg.OrdemServicoId, DateTime.UtcNow), ct);
            return;
        }

        try
        {
            // 1ª passada: valida tudo antes de mexer no saldo.
            var alvos = new List<(EstoquePeca estoque, int qtd)>();
            foreach (var item in msg.Pecas)
            {
                var est = await _estoque.ObterPorPecaIdAsync(item.PecaId, ct)
                    ?? throw new DomainException($"Peça {item.PecaId} sem estoque cadastrado.");
                if (est.QuantidadeDisponivel < item.Quantidade)
                    throw new DomainException($"Estoque insuficiente da peça {item.PecaId}. Disponível: {est.QuantidadeDisponivel}, solicitado: {item.Quantidade}.");
                alvos.Add((est, item.Quantidade));
            }

            // 2ª passada: só reserva depois que tudo passou na validação.
            foreach (var (est, qtd) in alvos)
            {
                est.Reservar(qtd, $"OS:{msg.OrdemServicoId}");
                await _reservas.AdicionarAsync(ReservaPeca.Criar(msg.OrdemServicoId, est.PecaId, qtd), ct);
            }
            if (alvos.Count > 0) await _uow.CommitAsync(ct);
            await context.Publish(new PecasReservadas(msg.OrdemServicoId, DateTime.UtcNow), ct);
        }
        catch (DomainException ex)
        {
            await context.Publish(new ReservaPecasFalhou(msg.OrdemServicoId, DateTime.UtcNow, ex.Message), ct);
        }
    }
}
