using Execucao.Application.Common;
using Execucao.Application.DTOs;
using Execucao.Application.Mapping;
using Execucao.Domain.Common;
using Execucao.Domain.Estoque;
using Execucao.Domain.Execucoes;
using Execucao.Domain.Reservas;
using MassTransit;
using Oficina.Contracts;
using ExecucaoOS = Execucao.Domain.Execucoes.Execucao;

namespace Execucao.Application.UseCases;

public sealed class ExecucaoUseCases
{
    private readonly IExecucaoRepository _repo;
    private readonly IReservaPecaRepository _reservas;
    private readonly IEstoquePecaRepository _estoque;
    private readonly IUnitOfWork _uow;
    private readonly IPublishEndpoint _bus;

    public ExecucaoUseCases(IExecucaoRepository repo, IReservaPecaRepository reservas,
        IEstoquePecaRepository estoque, IUnitOfWork uow, IPublishEndpoint bus)
    {
        _repo = repo;
        _reservas = reservas;
        _estoque = estoque;
        _uow = uow;
        _bus = bus;
    }

    public async Task<IReadOnlyList<ExecucaoResponse>> ListarFilaAsync(int skip, int take, CancellationToken ct = default)
        => (await _repo.ListarFilaAsync(skip, take, ct)).Select(e => e.ToResponse()).ToList();

    public async Task<ExecucaoResponse> ObterAsync(Guid id, CancellationToken ct = default)
        => (await Carregar(id, ct)).ToResponse();

    public async Task<ExecucaoResponse> IniciarAsync(Guid id, CancellationToken ct = default)
    {
        var e = await Carregar(id, ct);
        e.Iniciar();
        await _bus.Publish(new ExecucaoIniciada(e.OrdemServicoId, DateTime.UtcNow, e.Id), ct);
        await _uow.CommitAsync(ct);
        return e.ToResponse();
    }

    public async Task<ExecucaoResponse> RegistrarDiagnosticoAsync(Guid id, RegistrarDiagnosticoRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Observacoes))
            throw new DomainException("Observações do diagnóstico são obrigatórias.");

        var e = await Carregar(id, ct);
        e.RegistrarDiagnostico(req.Observacoes);
        await _uow.CommitAsync(ct);
        return e.ToResponse();
    }

    public async Task<ExecucaoResponse> FinalizarAsync(Guid id, CancellationToken ct = default)
    {
        var e = await Carregar(id, ct);

        // Reentrega sobre execução já Finalizada: devolve o estado sem publicar nem reabrir a transação.
        if (e.Status == StatusExecucao.Finalizada)
            return e.ToResponse();

        e.Finalizar();

        // Consumo atômico das reservas na finalização (não há consumo parcial);
        // só as Ativas mudam de status, então reentrega não quebra a idempotência.
        foreach (var r in (await _reservas.ListarPorOsAsync(e.OrdemServicoId, ct)).Where(r => r.Status == StatusReserva.Ativa))
        {
            var est = await _estoque.ObterPorPecaIdAsync(r.PecaId, ct)
                ?? throw new DomainException($"Estoque da peça {r.PecaId} não encontrado para consumir a reserva da OS {e.OrdemServicoId}.");
            est.ConsumirReservado(r.Quantidade, $"OS finalizada:{e.OrdemServicoId}");
            r.Consumir();
        }

        await _bus.Publish(new ExecucaoFinalizada(e.OrdemServicoId, DateTime.UtcNow, e.Id), ct);
        await _uow.CommitAsync(ct);
        return e.ToResponse();
    }

    public async Task<ExecucaoResponse> FalharAsync(Guid id, FalharExecucaoRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Motivo))
            throw new DomainException("Motivo da falha é obrigatório.");

        // As peças não são liberadas aqui: a Saga responde com LiberarPecas.
        var e = await Carregar(id, ct);

        // Reentrega sobre execução já Falhou: devolve o estado sem publicar nem reabrir a transação.
        if (e.Status == StatusExecucao.Falhou)
            return e.ToResponse();

        e.Falhar(req.Motivo);
        await _bus.Publish(new ExecucaoFalhou(e.OrdemServicoId, DateTime.UtcNow, e.Id, req.Motivo), ct);
        await _uow.CommitAsync(ct);
        return e.ToResponse();
    }

    private async Task<ExecucaoOS> Carregar(Guid id, CancellationToken ct)
        => await _repo.ObterPorIdAsync(id, ct) ?? throw new NotFoundException("Execução", id);
}
