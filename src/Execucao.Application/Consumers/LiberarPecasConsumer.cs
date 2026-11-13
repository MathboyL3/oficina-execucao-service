using Execucao.Domain.Common;
using Execucao.Domain.Estoque;
using Execucao.Domain.Execucoes;
using Execucao.Domain.Reservas;
using MassTransit;
using Oficina.Contracts;

namespace Execucao.Application.Consumers;

/// <summary>Compensação: libera reservas Ativas e marca a execução como Falhou. Sempre confirma.</summary>
public sealed class LiberarPecasConsumer : IConsumer<LiberarPecas>
{
    private readonly IEstoquePecaRepository _estoque;
    private readonly IReservaPecaRepository _reservas;
    private readonly IExecucaoRepository _execucoes;
    private readonly IUnitOfWork _uow;

    public LiberarPecasConsumer(IEstoquePecaRepository estoque, IReservaPecaRepository reservas,
        IExecucaoRepository execucoes, IUnitOfWork uow)
    {
        _estoque = estoque;
        _reservas = reservas;
        _execucoes = execucoes;
        _uow = uow;
    }

    public async Task Consume(ConsumeContext<LiberarPecas> context)
    {
        var msg = context.Message;
        var ct = context.CancellationToken;

        // Só reservas Ativas são estornadas: mensagem duplicada não pode lançar.
        foreach (var r in (await _reservas.ListarPorOsAsync(msg.OrdemServicoId, ct)).Where(r => r.Status == StatusReserva.Ativa))
        {
            var est = await _estoque.ObterPorPecaIdAsync(r.PecaId, ct);
            est?.EstornarReserva(r.Quantidade, $"OS cancelada:{msg.OrdemServicoId}");
            r.Liberar();
        }

        var exec = await _execucoes.ObterPorOsAsync(msg.OrdemServicoId, ct);
        if (exec is not null && exec.Status is not (StatusExecucao.Finalizada or StatusExecucao.Falhou))
            exec.Falhar(msg.Motivo);

        await _uow.CommitAsync(ct);
        await context.Publish(new PecasLiberadas(msg.OrdemServicoId, DateTime.UtcNow), ct);
    }
}
