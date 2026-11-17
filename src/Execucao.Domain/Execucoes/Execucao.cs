using Execucao.Domain.Common;

namespace Execucao.Domain.Execucoes;

public sealed record ServicoExecucao(Guid ServicoId, int Quantidade);

public sealed class Execucao : AggregateRoot
{
    public Guid OrdemServicoId { get; private set; }
    public StatusExecucao Status { get; private set; }
    public int Prioridade { get; private set; }
    public DateTime EnfileiradaEm { get; private set; }
    public DateTime? IniciadaEm { get; private set; }
    public DateTime? FinalizadaEm { get; private set; }
    public string? ObservacoesDiagnostico { get; private set; }
    public string? MotivoFalha { get; private set; }
    public List<ServicoExecucao> Servicos { get; private set; } = new();

    private Execucao() { }

    public static Execucao Enfileirar(Guid ordemServicoId, IEnumerable<(Guid servicoId, int qtd)> servicos, int prioridade = 0)
    {
        if (ordemServicoId == Guid.Empty) throw new DomainException("OS é obrigatória.");
        return new Execucao
        {
            OrdemServicoId = ordemServicoId,
            Status = StatusExecucao.NaFila,
            Prioridade = prioridade,
            EnfileiradaEm = DateTime.UtcNow,
            Servicos = servicos.Select(s => new ServicoExecucao(s.servicoId, s.qtd)).ToList()
        };
    }

    public void Iniciar()
    {
        Exigir(StatusExecucao.NaFila, StatusExecucao.EmDiagnostico);
        IniciadaEm = DateTime.UtcNow;
    }

    public void RegistrarDiagnostico(string observacoes)
    {
        Exigir(StatusExecucao.EmDiagnostico, StatusExecucao.EmReparo);
        ObservacoesDiagnostico = observacoes;
    }

    public void Finalizar()
    {
        Exigir(StatusExecucao.EmReparo, StatusExecucao.Finalizada);
        FinalizadaEm = DateTime.UtcNow;
    }

    public void Falhar(string motivo)
    {
        if (Status is StatusExecucao.Finalizada or StatusExecucao.Falhou)
            throw new DomainException($"Execução no status {Status} não pode falhar.");
        Status = StatusExecucao.Falhou;
        MotivoFalha = motivo;
    }

    private void Exigir(StatusExecucao de, StatusExecucao para)
    {
        if (Status != de)
            throw new DomainException($"Transição inválida: status atual é {Status}, esperado {de} para mudar para {para}.");
        Status = para;
    }
}
