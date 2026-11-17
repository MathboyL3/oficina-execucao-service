using Execucao.Application.Common;
using Execucao.Application.DTOs;
using Execucao.Application.Mapping;
using Execucao.Domain.Catalogo;
using Execucao.Domain.Common;
using Execucao.Domain.ValueObjects;

namespace Execucao.Application.UseCases;

public sealed class ServicoUseCases
{
    private readonly IServicoRepository _repo;
    private readonly IUnitOfWork _uow;

    public ServicoUseCases(IServicoRepository repo, IUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<ServicoResponse> CadastrarAsync(CadastrarServicoRequest req, CancellationToken ct = default)
    {
        var s = Servico.Cadastrar(req.Nome, req.Descricao, Money.Brl(req.ValorBase), TimeSpan.FromMinutes(req.TempoEstimadoMinutos));
        await _repo.AdicionarAsync(s, ct);
        await _uow.CommitAsync(ct);
        return s.ToResponse();
    }

    public async Task<ServicoResponse> ObterAsync(Guid id, CancellationToken ct = default)
    {
        var s = await _repo.ObterPorIdAsync(id, ct) ?? throw new NotFoundException("Serviço", id);
        return s.ToResponse();
    }

    public async Task<IReadOnlyList<ServicoResponse>> ListarAsync(bool somenteAtivos, CancellationToken ct = default)
    {
        var list = await _repo.ListarAsync(somenteAtivos, ct);
        return list.Select(s => s.ToResponse()).ToList();
    }

    public async Task<ServicoResponse> AtualizarAsync(Guid id, AtualizarServicoRequest req, CancellationToken ct = default)
    {
        var s = await _repo.ObterPorIdAsync(id, ct) ?? throw new NotFoundException("Serviço", id);
        s.Atualizar(
            req.Nome,
            req.Descricao,
            req.ValorBase.HasValue ? Money.Brl(req.ValorBase.Value) : null,
            req.TempoEstimadoMinutos.HasValue ? TimeSpan.FromMinutes(req.TempoEstimadoMinutos.Value) : null);
        await _uow.CommitAsync(ct);
        return s.ToResponse();
    }

    public async Task InativarAsync(Guid id, CancellationToken ct = default)
    {
        var s = await _repo.ObterPorIdAsync(id, ct) ?? throw new NotFoundException("Serviço", id);
        s.Inativar();
        await _uow.CommitAsync(ct);
    }
}
