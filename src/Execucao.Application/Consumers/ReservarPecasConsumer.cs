using Execucao.Domain.Common;
using Execucao.Domain.Estoque;
using Execucao.Domain.Reservas;
using MassTransit;
using Microsoft.Extensions.Logging;
using Oficina.Contracts;

namespace Execucao.Application.Consumers;

/// <summary>Reserva todas as peças da OS (tudo ou nada). Idempotente por OS e recusada depois da liberação.</summary>
public sealed class ReservarPecasConsumer : IConsumer<ReservarPecas>
{
    private readonly IEstoquePecaRepository _estoque;
    private readonly IReservaPecaRepository _reservas;
    private readonly ILiberacaoOsRepository _liberacoes;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<ReservarPecasConsumer> _logger;

    public ReservarPecasConsumer(IEstoquePecaRepository estoque, IReservaPecaRepository reservas,
        ILiberacaoOsRepository liberacoes, IUnitOfWork uow, ILogger<ReservarPecasConsumer> logger)
    {
        _estoque = estoque;
        _reservas = reservas;
        _liberacoes = liberacoes;
        _uow = uow;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ReservarPecas> context)
    {
        var msg = context.Message;
        var ct = context.CancellationToken;

        // Ordem invertida: a liberação pode chegar antes da reserva; a OS cancelada não reserva mais.
        if (await _liberacoes.ExisteAsync(msg.OrdemServicoId, ct))
        {
            await PublicarFalha(context, msg.OrdemServicoId, "OS já liberada/cancelada.", ct);
            return;
        }

        // Idempotência: qualquer reserva já registrada para a OS encerra o consumo.
        var existentes = await _reservas.ListarPorOsAsync(msg.OrdemServicoId, ct);
        if (existentes.Count > 0)
        {
            if (existentes.Any(r => r.Status == StatusReserva.Ativa))
                await context.Publish(new PecasReservadas(msg.OrdemServicoId, DateTime.UtcNow), ct);
            else
                _logger.LogInformation("OS {OrdemServicoId} já teve reservas encerradas: nada a reservar.", msg.OrdemServicoId);
            return;
        }

        try
        {
            // 1ª passada: agrupa por peça e valida quantidade e saldo antes de mexer em qualquer saldo.
            var alvos = new Dictionary<Guid, int>();
            foreach (var item in msg.Pecas ?? Array.Empty<ItemPecaQtd>())
            {
                if (item.Quantidade <= 0)
                    throw new DomainException($"Quantidade da peça {item.PecaId} deve ser positiva.");
                alvos[item.PecaId] = alvos.GetValueOrDefault(item.PecaId) + item.Quantidade;
            }

            var reservas = new List<(EstoquePeca estoque, int qtd)>();
            foreach (var (pecaId, qtd) in alvos)
            {
                var est = await _estoque.ObterPorPecaIdAsync(pecaId, ct)
                    ?? throw new DomainException($"Peça {pecaId} sem estoque cadastrado.");
                if (est.QuantidadeDisponivel < qtd)
                    throw new DomainException($"Estoque insuficiente da peça {pecaId}. Disponível: {est.QuantidadeDisponivel}, solicitado: {qtd}.");
                reservas.Add((est, qtd));
            }

            // 2ª passada: só reserva depois que tudo passou na validação.
            foreach (var (est, qtd) in reservas)
            {
                est.Reservar(qtd, $"OS:{msg.OrdemServicoId}");
                await _reservas.AdicionarAsync(ReservaPeca.Criar(msg.OrdemServicoId, est.PecaId, qtd), ct);
            }
            if (reservas.Count > 0) await _uow.CommitAsync(ct);
            await context.Publish(new PecasReservadas(msg.OrdemServicoId, DateTime.UtcNow), ct);
        }
        catch (DomainException ex)
        {
            await PublicarFalha(context, msg.OrdemServicoId, ex.Message, ct);
        }
    }

    /// <summary>Publica a falha e confirma: sem commit o outbox não grava a mensagem. Nada foi mutado até aqui.</summary>
    private async Task PublicarFalha(ConsumeContext<ReservarPecas> context, Guid osId, string motivo, CancellationToken ct)
    {
        await context.Publish(new ReservaPecasFalhou(osId, DateTime.UtcNow, motivo), ct);
        await _uow.CommitAsync(ct);
    }
}
