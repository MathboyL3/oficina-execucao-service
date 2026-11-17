using Execucao.Domain.Common;
using Execucao.Domain.Estoque;
using Execucao.Domain.Execucoes;
using Execucao.Domain.Reservas;
using MassTransit;
using Oficina.Contracts;

namespace Execucao.Application.Consumers;

/// <summary>Compensação: estorna reservas Ativas, registra a liberação e marca a execução como Falhou. Sempre confirma.</summary>
public sealed class LiberarPecasConsumer : IConsumer<LiberarPecas>
{
    private readonly IEstoquePecaRepository _estoque;
    private readonly IReservaPecaRepository _reservas;
    private readonly IExecucaoRepository _execucoes;
    private readonly ILiberacaoOsRepository _liberacoes;
    private readonly IUnitOfWork _uow;

    public LiberarPecasConsumer(IEstoquePecaRepository estoque, IReservaPecaRepository reservas,
        IExecucaoRepository execucoes, ILiberacaoOsRepository liberacoes, IUnitOfWork uow)
    {
        _estoque = estoque;
        _reservas = reservas;
        _execucoes = execucoes;
        _liberacoes = liberacoes;
        _uow = uow;
    }

    public async Task Consume(ConsumeContext<LiberarPecas> context)
    {
        var msg = context.Message;
        var ct = context.CancellationToken;

        // 1ª passada, só leitura: só reservas Ativas são estornadas (mensagem duplicada não
        // pode lançar) e todos os estoques são resolvidos antes de qualquer mutação.
        var alvos = new List<(EstoquePeca estoque, ReservaPeca reserva)>();
        foreach (var r in (await _reservas.ListarPorOsAsync(msg.OrdemServicoId, ct)).Where(r => r.Status == StatusReserva.Ativa))
        {
            var est = await _estoque.ObterPorPecaIdAsync(r.PecaId, ct)
                ?? throw new DomainException($"Estoque da peça {r.PecaId} não encontrado para estornar a reserva da OS {msg.OrdemServicoId}.");
            alvos.Add((est, r));
        }

        // Marcador gravado (mesmo sem reservas) ANTES do estorno: uma reserva fora de ordem
        // depois disto é recusada por ReservarPecas.
        if (!await _liberacoes.ExisteAsync(msg.OrdemServicoId, ct))
            await _liberacoes.AdicionarAsync(LiberacaoOs.Criar(msg.OrdemServicoId), ct);

        // 2ª passada: com tudo validado, estorna e libera.
        foreach (var (est, r) in alvos)
        {
            est.EstornarReserva(r.Quantidade, $"OS cancelada:{msg.OrdemServicoId}");
            r.Liberar();
        }

        var exec = await _execucoes.ObterPorOsAsync(msg.OrdemServicoId, ct);
        if (exec is not null && exec.Status is not (StatusExecucao.Finalizada or StatusExecucao.Falhou))
            exec.Falhar(msg.Motivo);

        // Publica antes do SaveChanges: o outbox só entrega o que entrou no commit.
        await context.Publish(new PecasLiberadas(msg.OrdemServicoId, DateTime.UtcNow), ct);
        await _uow.CommitAsync(ct);
    }
}
