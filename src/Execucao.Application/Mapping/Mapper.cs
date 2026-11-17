using Execucao.Application.DTOs;
using Execucao.Domain.Catalogo;
using Execucao.Domain.Estoque;
using ExecucaoOS = Execucao.Domain.Execucoes.Execucao;

namespace Execucao.Application.Mapping;

public static class Mapper
{
    public static ServicoResponse ToResponse(this Servico s) => new(
        s.Id, s.Nome, s.Descricao, s.ValorBase.Amount, (int)s.TempoEstimado.TotalMinutes, s.Ativo);

    public static PecaResponse ToResponse(this Peca p, EstoquePeca? e) => new(
        p.Id, p.Nome, p.Codigo, p.Unidade, p.Valor.Amount, p.Ativo,
        e?.QuantidadeDisponivel ?? 0,
        e?.QuantidadeReservada ?? 0,
        e?.LimiteMinimo ?? 0,
        e?.EstoqueBaixo ?? false);

    public static ExecucaoResponse ToResponse(this ExecucaoOS e) => new(
        e.Id, e.OrdemServicoId, e.Status.ToString(), e.Prioridade, e.EnfileiradaEm,
        e.IniciadaEm, e.FinalizadaEm, e.ObservacoesDiagnostico, e.MotivoFalha);
}
