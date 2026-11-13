namespace Execucao.Application.DTOs;

public sealed record ExecucaoResponse(Guid Id, Guid OrdemServicoId, string Status, int Prioridade,
    DateTime EnfileiradaEm, DateTime? IniciadaEm, DateTime? FinalizadaEm, string? ObservacoesDiagnostico, string? MotivoFalha);

public sealed record RegistrarDiagnosticoRequest(string Observacoes);

public sealed record FalharExecucaoRequest(string Motivo);
