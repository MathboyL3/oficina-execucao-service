using Execucao.Application.Consumers;
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

public class ConsumersTests
{
    private readonly Mock<IEstoquePecaRepository> _estoque = new();
    private readonly Mock<IReservaPecaRepository> _reservas = new();
    private readonly Mock<IExecucaoRepository> _execucoes = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private static Mock<ConsumeContext<T>> Ctx<T>(T msg) where T : class
    {
        var ctx = new Mock<ConsumeContext<T>>();
        ctx.SetupGet(c => c.Message).Returns(msg);
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return ctx;
    }

    [Fact]
    public async Task Reservar_com_estoque_suficiente_reserva_tudo_e_publica_PecasReservadas()
    {
        var os = Guid.NewGuid();
        var p1 = EstoquePeca.Criar(Guid.NewGuid(), 5);
        var p2 = EstoquePeca.Criar(Guid.NewGuid(), 5);
        _estoque.Setup(e => e.ObterPorPecaIdAsync(p1.PecaId, It.IsAny<CancellationToken>())).ReturnsAsync(p1);
        _estoque.Setup(e => e.ObterPorPecaIdAsync(p2.PecaId, It.IsAny<CancellationToken>())).ReturnsAsync(p2);
        _reservas.Setup(r => r.ListarPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ReservaPeca>());
        var ctx = Ctx(new ReservarPecas(os, DateTime.UtcNow, new[] { new ItemPecaQtd(p1.PecaId, 2), new ItemPecaQtd(p2.PecaId, 3) }));

        await new ReservarPecasConsumer(_estoque.Object, _reservas.Object, _uow.Object).Consume(ctx.Object);

        p1.QuantidadeReservada.Should().Be(2);
        p2.QuantidadeReservada.Should().Be(3);
        _reservas.Verify(r => r.AdicionarAsync(It.IsAny<ReservaPeca>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        ctx.Verify(c => c.Publish(It.Is<PecasReservadas>(m => m.OrdemServicoId == os), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reservar_com_uma_peca_sem_estoque_nao_reserva_nenhuma()
    {
        var os = Guid.NewGuid();
        var ok = EstoquePeca.Criar(Guid.NewGuid(), 5);
        var falta = EstoquePeca.Criar(Guid.NewGuid(), 1);
        _estoque.Setup(e => e.ObterPorPecaIdAsync(ok.PecaId, It.IsAny<CancellationToken>())).ReturnsAsync(ok);
        _estoque.Setup(e => e.ObterPorPecaIdAsync(falta.PecaId, It.IsAny<CancellationToken>())).ReturnsAsync(falta);
        _reservas.Setup(r => r.ListarPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ReservaPeca>());
        var ctx = Ctx(new ReservarPecas(os, DateTime.UtcNow, new[] { new ItemPecaQtd(ok.PecaId, 2), new ItemPecaQtd(falta.PecaId, 3) }));

        await new ReservarPecasConsumer(_estoque.Object, _reservas.Object, _uow.Object).Consume(ctx.Object);

        ok.QuantidadeReservada.Should().Be(0);
        ok.QuantidadeDisponivel.Should().Be(5);
        falta.QuantidadeReservada.Should().Be(0);
        _reservas.Verify(r => r.AdicionarAsync(It.IsAny<ReservaPeca>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        ctx.Verify(c => c.Publish(It.Is<ReservaPecasFalhou>(m => m.OrdemServicoId == os && m.Motivo.Contains("insuficiente")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reservar_peca_inexistente_publica_falha()
    {
        var os = Guid.NewGuid();
        _reservas.Setup(r => r.ListarPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ReservaPeca>());
        var ctx = Ctx(new ReservarPecas(os, DateTime.UtcNow, new[] { new ItemPecaQtd(Guid.NewGuid(), 1) }));

        await new ReservarPecasConsumer(_estoque.Object, _reservas.Object, _uow.Object).Consume(ctx.Object);

        ctx.Verify(c => c.Publish(It.IsAny<ReservaPecasFalhou>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reservar_duplicado_so_republica_sucesso()
    {
        var os = Guid.NewGuid();
        _reservas.Setup(r => r.ListarPorOsAsync(os, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReservaPeca> { ReservaPeca.Criar(os, Guid.NewGuid(), 1) });
        var ctx = Ctx(new ReservarPecas(os, DateTime.UtcNow, new[] { new ItemPecaQtd(Guid.NewGuid(), 1) }));

        await new ReservarPecasConsumer(_estoque.Object, _reservas.Object, _uow.Object).Consume(ctx.Object);

        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        ctx.Verify(c => c.Publish(It.IsAny<PecasReservadas>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reservar_sem_pecas_publica_sucesso_direto()
    {
        var os = Guid.NewGuid();
        _reservas.Setup(r => r.ListarPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ReservaPeca>());
        var ctx = Ctx(new ReservarPecas(os, DateTime.UtcNow, Array.Empty<ItemPecaQtd>()));

        await new ReservarPecasConsumer(_estoque.Object, _reservas.Object, _uow.Object).Consume(ctx.Object);

        ctx.Verify(c => c.Publish(It.IsAny<PecasReservadas>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Liberar_estorna_so_ativas_marca_execucao_falhou_e_confirma()
    {
        var os = Guid.NewGuid();
        var est = EstoquePeca.Criar(Guid.NewGuid(), 5);
        est.Reservar(2, "x");
        var ativa = ReservaPeca.Criar(os, est.PecaId, 2);
        var consumida = ReservaPeca.Criar(os, est.PecaId, 1);
        consumida.Consumir();
        _reservas.Setup(r => r.ListarPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ReservaPeca> { ativa, consumida });
        _estoque.Setup(e => e.ObterPorPecaIdAsync(est.PecaId, It.IsAny<CancellationToken>())).ReturnsAsync(est);
        var exec = ExecucaoOS.Enfileirar(os, Array.Empty<(Guid, int)>());
        _execucoes.Setup(e => e.ObterPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(exec);
        var ctx = Ctx(new LiberarPecas(os, DateTime.UtcNow, "cancelada"));

        await new LiberarPecasConsumer(_estoque.Object, _reservas.Object, _execucoes.Object, _uow.Object).Consume(ctx.Object);

        ativa.Status.Should().Be(StatusReserva.Liberada);
        consumida.Status.Should().Be(StatusReserva.Consumida);
        est.QuantidadeReservada.Should().Be(0);
        exec.Status.Should().Be(StatusExecucao.Falhou);
        ctx.Verify(c => c.Publish(It.Is<PecasLiberadas>(m => m.OrdemServicoId == os), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Liberar_sem_reserva_nem_execucao_ainda_confirma()
    {
        var os = Guid.NewGuid();
        _reservas.Setup(r => r.ListarPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ReservaPeca>());
        var ctx = Ctx(new LiberarPecas(os, DateTime.UtcNow, "x"));

        await new LiberarPecasConsumer(_estoque.Object, _reservas.Object, _execucoes.Object, _uow.Object).Consume(ctx.Object);

        ctx.Verify(c => c.Publish(It.IsAny<PecasLiberadas>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Liberar_duplicado_nao_lanca_e_republica_confirmacao()
    {
        var os = Guid.NewGuid();
        var est = EstoquePeca.Criar(Guid.NewGuid(), 5);
        est.Reservar(2, "x");
        var ativa = ReservaPeca.Criar(os, est.PecaId, 2);
        _reservas.Setup(r => r.ListarPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ReservaPeca> { ativa });
        _estoque.Setup(e => e.ObterPorPecaIdAsync(est.PecaId, It.IsAny<CancellationToken>())).ReturnsAsync(est);
        var exec = ExecucaoOS.Enfileirar(os, Array.Empty<(Guid, int)>());
        _execucoes.Setup(e => e.ObterPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(exec);
        var consumer = new LiberarPecasConsumer(_estoque.Object, _reservas.Object, _execucoes.Object, _uow.Object);
        var ctx = Ctx(new LiberarPecas(os, DateTime.UtcNow, "cancelada"));

        await consumer.Consume(ctx.Object);
        await consumer.Consume(ctx.Object);

        ativa.Status.Should().Be(StatusReserva.Liberada);
        est.QuantidadeReservada.Should().Be(0);
        est.QuantidadeDisponivel.Should().Be(5);
        exec.Status.Should().Be(StatusExecucao.Falhou);
        ctx.Verify(c => c.Publish(It.IsAny<PecasLiberadas>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Enfileirar_cria_execucao_uma_vez()
    {
        var os = Guid.NewGuid();
        var ctx = Ctx(new EnfileirarExecucao(os, DateTime.UtcNow, new[] { new ItemServicoQtd(Guid.NewGuid(), 1) }, Array.Empty<ItemPecaQtd>()));
        await new EnfileirarExecucaoConsumer(_execucoes.Object, _uow.Object).Consume(ctx.Object);
        _execucoes.Verify(e => e.AdicionarAsync(It.Is<ExecucaoOS>(x => x.OrdemServicoId == os), It.IsAny<CancellationToken>()), Times.Once);

        _execucoes.Setup(e => e.ObterPorOsAsync(os, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExecucaoOS.Enfileirar(os, Array.Empty<(Guid, int)>()));
        await new EnfileirarExecucaoConsumer(_execucoes.Object, _uow.Object).Consume(ctx.Object);
        _execucoes.Verify(e => e.AdicionarAsync(It.IsAny<ExecucaoOS>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
