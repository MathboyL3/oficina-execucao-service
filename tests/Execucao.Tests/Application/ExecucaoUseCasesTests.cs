using Execucao.Application.Common;
using Execucao.Application.DTOs;
using Execucao.Application.UseCases;
using Execucao.Domain.Common;
using Execucao.Domain.Estoque;
using Execucao.Domain.Execucoes;
using Execucao.Domain.Reservas;
using FluentAssertions;
using MassTransit;
using Moq;
using Oficina.Contracts;
using ExecucaoOS = Execucao.Domain.Execucoes.Execucao;

namespace Execucao.Tests.Application;

public class ExecucaoUseCasesTests
{
    private readonly Mock<IExecucaoRepository> _repo = new();
    private readonly Mock<IReservaPecaRepository> _reservas = new();
    private readonly Mock<IEstoquePecaRepository> _estoque = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IPublishEndpoint> _bus = new();
    private ExecucaoUseCases Uc() => new(_repo.Object, _reservas.Object, _estoque.Object, _uow.Object, _bus.Object);

    private ExecucaoOS Setup(ExecucaoOS e)
    {
        _repo.Setup(r => r.ObterPorIdAsync(e.Id, It.IsAny<CancellationToken>())).ReturnsAsync(e);
        return e;
    }

    [Fact]
    public async Task Iniciar_publica_ExecucaoIniciada()
    {
        var e = Setup(ExecucaoOS.Enfileirar(Guid.NewGuid(), Array.Empty<(Guid, int)>()));
        var r = await Uc().IniciarAsync(e.Id);
        r.Status.Should().Be("EmDiagnostico");
        _bus.Verify(b => b.Publish(It.Is<ExecucaoIniciada>(m => m.ExecucaoId == e.Id && m.OrdemServicoId == e.OrdemServicoId), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Finalizar_consome_reservas_ativas_e_publica()
    {
        var os = Guid.NewGuid();
        var e = Setup(ExecucaoOS.Enfileirar(os, Array.Empty<(Guid, int)>()));
        e.Iniciar(); e.RegistrarDiagnostico("ok");
        var est = EstoquePeca.Criar(Guid.NewGuid(), 5); est.Reservar(2, "x");
        var res = ReservaPeca.Criar(os, est.PecaId, 2);
        _reservas.Setup(r => r.ListarPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ReservaPeca> { res });
        _estoque.Setup(x => x.ObterPorPecaIdAsync(est.PecaId, It.IsAny<CancellationToken>())).ReturnsAsync(est);

        var r = await Uc().FinalizarAsync(e.Id);

        r.Status.Should().Be("Finalizada");
        res.Status.Should().Be(StatusReserva.Consumida);
        est.QuantidadeReservada.Should().Be(0);
        est.QuantidadeDisponivel.Should().Be(3);
        _bus.Verify(b => b.Publish(It.Is<ExecucaoFinalizada>(m => m.OrdemServicoId == os), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Falhar_publica_ExecucaoFalhou_sem_liberar_pecas()
    {
        var e = Setup(ExecucaoOS.Enfileirar(Guid.NewGuid(), Array.Empty<(Guid, int)>()));
        await Uc().FalharAsync(e.Id, new FalharExecucaoRequest("motor fundido"));
        _bus.Verify(b => b.Publish(It.Is<ExecucaoFalhou>(m => m.Motivo == "motor fundido"), It.IsAny<CancellationToken>()), Times.Once);
        _reservas.Verify(r => r.ListarPorOsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Diagnostico_e_listagem()
    {
        var e = Setup(ExecucaoOS.Enfileirar(Guid.NewGuid(), Array.Empty<(Guid, int)>()));
        e.Iniciar();
        (await Uc().RegistrarDiagnosticoAsync(e.Id, new RegistrarDiagnosticoRequest("freio"))).Status.Should().Be("EmReparo");
        _repo.Setup(r => r.ListarFilaAsync(0, 20, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ExecucaoOS> { e });
        (await Uc().ListarFilaAsync(0, 20)).Should().HaveCount(1);
        (await Uc().ObterAsync(e.Id)).Id.Should().Be(e.Id);
    }

    [Fact]
    public async Task Inexistente_lanca_NotFound()
        => await FluentActions.Awaiting(() => Uc().IniciarAsync(Guid.NewGuid())).Should().ThrowAsync<NotFoundException>();
}
