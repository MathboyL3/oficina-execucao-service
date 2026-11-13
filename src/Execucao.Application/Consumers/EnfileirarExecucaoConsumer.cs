using Execucao.Domain.Common;
using Execucao.Domain.Execucoes;
using MassTransit;
using Oficina.Contracts;
using ExecucaoOS = Execucao.Domain.Execucoes.Execucao;

namespace Execucao.Application.Consumers;

/// <summary>Coloca a OS na fila de execução. Idempotente: uma execução por OS.</summary>
public sealed class EnfileirarExecucaoConsumer : IConsumer<EnfileirarExecucao>
{
    private readonly IExecucaoRepository _execucoes;
    private readonly IUnitOfWork _uow;

    public EnfileirarExecucaoConsumer(IExecucaoRepository execucoes, IUnitOfWork uow)
    {
        _execucoes = execucoes;
        _uow = uow;
    }

    public async Task Consume(ConsumeContext<EnfileirarExecucao> context)
    {
        var msg = context.Message;
        var ct = context.CancellationToken;

        if (await _execucoes.ObterPorOsAsync(msg.OrdemServicoId, ct) is not null)
            return;

        var exec = ExecucaoOS.Enfileirar(msg.OrdemServicoId,
            (msg.Servicos ?? Array.Empty<ItemServicoQtd>()).Select(s => (s.ServicoId, s.Quantidade)));
        await _execucoes.AdicionarAsync(exec, ct);
        await _uow.CommitAsync(ct);
    }
}
