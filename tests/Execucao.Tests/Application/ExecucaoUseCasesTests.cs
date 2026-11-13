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
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Finalizar_consome_so_reservas_ativas()
    {
        var os = Guid.NewGuid();
        var e = Setup(ExecucaoOS.Enfileirar(os, Array.Empty<(Guid, int)>()));
        e.Iniciar(); e.RegistrarDiagnostico("ok");
        var est = EstoquePeca.Criar(Guid.NewGuid(), 5); est.Reservar(2, "x");
        var ativa = ReservaPeca.Criar(os, est.PecaId, 2);
        var consumida = ReservaPeca.Criar(os, Guid.NewGuid(), 1); consumida.Consumir();
        var liberada = ReservaPeca.Criar(os, Guid.NewGuid(), 1); liberada.Liberar();
        _reservas.Setup(r => r.ListarPorOsAsync(os, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReservaPeca> { ativa, consumida, liberada });
        _estoque.Setup(x => x.ObterPorPecaIdAsync(est.PecaId, It.IsAny<CancellationToken>())).ReturnsAsync(est);

        var act = async () => await Uc().FinalizarAsync(e.Id);

        await act.Should().NotThrowAsync();
        ativa.Status.Should().Be(StatusReserva.Consumida);
        consumida.Status.Should().Be(StatusReserva.Consumida);
        liberada.Status.Should().Be(StatusReserva.Liberada);
        _estoque.Verify(x => x.ObterPorPecaIdAsync(est.PecaId, It.IsAny<CancellationToken>()), Times.Once);
        _estoque.Verify(x => x.ObterPorPecaIdAsync(consumida.PecaId, It.IsAny<CancellationToken>()), Times.Never);
        _estoque.Verify(x => x.ObterPorPecaIdAsync(liberada.PecaId, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Finalizar_reserva_ativa_sem_estoque_lanca_DomainException()
    {
        var os = Guid.NewGuid();
        var e = Setup(ExecucaoOS.Enfileirar(os, Array.Empty<(Guid, int)>()));
        e.Iniciar(); e.RegistrarDiagnostico("ok");
        var ativa = ReservaPeca.Criar(os, Guid.NewGuid(), 2);
        _reservas.Setup(r => r.ListarPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ReservaPeca> { ativa });

        var act = async () => await Uc().FinalizarAsync(e.Id);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*estoque*");
        ativa.Status.Should().Be(StatusReserva.Ativa);
        _bus.Verify(b => b.Publish(It.IsAny<ExecucaoFinalizada>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Finalizar_ja_finalizada_devolve_estado_sem_publicar_nem_commit()
    {
        var e = Setup(ExecucaoOS.Enfileirar(Guid.NewGuid(), Array.Empty<(Guid, int)>()));
        e.Iniciar(); e.RegistrarDiagnostico("ok"); e.Finalizar();

        var r = await Uc().FinalizarAsync(e.Id);

        r.Status.Should().Be("Finalizada");
        e.Status.Should().Be(StatusExecucao.Finalizada);
        _reservas.Verify(x => x.ListarPorOsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _bus.Verify(b => b.Publish(It.IsAny<ExecucaoFinalizada>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Falhar_ja_falhou_devolve_estado_sem_publicar_nem_commit()
    {
        var e = Setup(ExecucaoOS.Enfileirar(Guid.NewGuid(), Array.Empty<(Guid, int)>()));
        e.Falhar("motor fundido");

        var r = await Uc().FalharAsync(e.Id, new FalharExecucaoRequest("outro motivo"));

        r.Status.Should().Be("Falhou");
        r.MotivoFalha.Should().Be("motor fundido");
        _bus.Verify(b => b.Publish(It.IsAny<ExecucaoFalhou>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task Falhar_sem_motivo_lanca_validacao(string? motivo)
    {
        var e = Setup(ExecucaoOS.Enfileirar(Guid.NewGuid(), Array.Empty<(Guid, int)>()));

        var act = async () => await Uc().FalharAsync(e.Id, new FalharExecucaoRequest(motivo!));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*motivo*");
        e.Status.Should().Be(StatusExecucao.NaFila);
        _bus.Verify(b => b.Publish(It.IsAny<ExecucaoFalhou>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task Diagnostico_sem_observacoes_lanca_validacao(string? observacoes)
    {
        var e = Setup(ExecucaoOS.Enfileirar(Guid.NewGuid(), Array.Empty<(Guid, int)>()));
        e.Iniciar();

        var act = async () => await Uc().RegistrarDiagnosticoAsync(e.Id, new RegistrarDiagnosticoRequest(observacoes!));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*observa*");
        e.Status.Should().Be(StatusExecucao.EmDiagnostico);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
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
