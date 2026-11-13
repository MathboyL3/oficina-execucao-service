using Execucao.Domain.Common;
using Execucao.Domain.Execucoes;
using FluentAssertions;
using ExecucaoOS = Execucao.Domain.Execucoes.Execucao;

namespace Execucao.Tests.Domain;

public class ExecucaoTests
{
    private static ExecucaoOS Nova() => ExecucaoOS.Enfileirar(Guid.NewGuid(), new[] { (Guid.NewGuid(), 1) });

    [Fact]
    public void Fluxo_completo_fila_diagnostico_reparo_finalizada()
    {
        var e = Nova();
        e.Status.Should().Be(StatusExecucao.NaFila);
        e.Iniciar();
        e.Status.Should().Be(StatusExecucao.EmDiagnostico);
        e.IniciadaEm.Should().NotBeNull();
        e.RegistrarDiagnostico("pastilha gasta");
        e.Status.Should().Be(StatusExecucao.EmReparo);
        e.ObservacoesDiagnostico.Should().Be("pastilha gasta");
        e.Finalizar();
        e.Status.Should().Be(StatusExecucao.Finalizada);
        e.FinalizadaEm.Should().NotBeNull();
    }

    [Fact]
    public void Transicoes_invalidas_lancam()
    {
        var e = Nova();
        FluentActions.Invoking(e.Finalizar).Should().Throw<DomainException>();
        FluentActions.Invoking(() => e.RegistrarDiagnostico("x")).Should().Throw<DomainException>();
        e.Falhar("cliente desistiu");
        e.Status.Should().Be(StatusExecucao.Falhou);
        e.MotivoFalha.Should().Be("cliente desistiu");
        FluentActions.Invoking(() => e.Falhar("de novo")).Should().Throw<DomainException>();
        FluentActions.Invoking(e.Iniciar).Should().Throw<DomainException>();
    }

    [Fact]
    public void Enfileirar_exige_os()
        => FluentActions.Invoking(() => ExecucaoOS.Enfileirar(Guid.Empty, Array.Empty<(Guid, int)>()))
            .Should().Throw<DomainException>();
}
