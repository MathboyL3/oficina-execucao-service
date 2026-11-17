using Execucao.Application.Consumers;
using Execucao.Domain.Common;
using Execucao.Domain.Estoque;
using Execucao.Domain.Execucoes;
using Execucao.Domain.Reservas;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oficina.Contracts;
using ExecucaoOS = Execucao.Domain.Execucoes.Execucao;

namespace Execucao.Tests.Application;

public class ConsumersTests
{
    private readonly Mock<IEstoquePecaRepository> _estoque = new();
    private readonly Mock<IReservaPecaRepository> _reservas = new();
    private readonly Mock<IExecucaoRepository> _execucoes = new();
    private readonly Mock<ILiberacaoOsRepository> _liberacoes = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private static Mock<ConsumeContext<T>> Ctx<T>(T msg) where T : class
    {
        var ctx = new Mock<ConsumeContext<T>>();
        ctx.SetupGet(c => c.Message).Returns(msg);
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return ctx;
    }

    private ReservarPecasConsumer Reservar() =>
        new(_estoque.Object, _reservas.Object, _liberacoes.Object, _uow.Object, NullLogger<ReservarPecasConsumer>.Instance);

    private LiberarPecasConsumer Liberar() =>
        new(_estoque.Object, _reservas.Object, _execucoes.Object, _liberacoes.Object, _uow.Object);

    private EnfileirarExecucaoConsumer Enfileirar() => new(_execucoes.Object, _uow.Object);

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
        var ordem = new List<string>();
        ctx.Setup(c => c.Publish(It.IsAny<PecasReservadas>(), It.IsAny<CancellationToken>()))
            .Callback(() => ordem.Add("publicar")).Returns(Task.CompletedTask);
        _uow.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
            .Callback(() => ordem.Add("commit")).ReturnsAsync(1);

        await Reservar().Consume(ctx.Object);

        // O outbox só entrega o PecasReservadas se ele entrar no SaveChanges.
        ordem.Should().Equal("publicar", "commit");
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

        await Reservar().Consume(ctx.Object);

        ok.QuantidadeReservada.Should().Be(0);
        ok.QuantidadeDisponivel.Should().Be(5);
        falta.QuantidadeReservada.Should().Be(0);
        _reservas.Verify(r => r.AdicionarAsync(It.IsAny<ReservaPeca>(), It.IsAny<CancellationToken>()), Times.Never);
        // O commit existe só para persistir o ReservaPecasFalhou (outbox); nada foi mutado.
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        ctx.Verify(c => c.Publish(It.Is<ReservaPecasFalhou>(m => m.OrdemServicoId == os && m.Motivo.Contains("insuficiente")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reservar_peca_inexistente_publica_falha()
    {
        var os = Guid.NewGuid();
        _reservas.Setup(r => r.ListarPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ReservaPeca>());
        var ctx = Ctx(new ReservarPecas(os, DateTime.UtcNow, new[] { new ItemPecaQtd(Guid.NewGuid(), 1) }));

        await Reservar().Consume(ctx.Object);

        ctx.Verify(c => c.Publish(It.IsAny<ReservaPecasFalhou>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reservar_soma_quantidades_da_mesma_peca_antes_de_validar()
    {
        var os = Guid.NewGuid();
        var p = EstoquePeca.Criar(Guid.NewGuid(), 3);
        _estoque.Setup(e => e.ObterPorPecaIdAsync(p.PecaId, It.IsAny<CancellationToken>())).ReturnsAsync(p);
        _reservas.Setup(r => r.ListarPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ReservaPeca>());
        var ctx = Ctx(new ReservarPecas(os, DateTime.UtcNow, new[] { new ItemPecaQtd(p.PecaId, 2), new ItemPecaQtd(p.PecaId, 2) }));

        await Reservar().Consume(ctx.Object);

        p.QuantidadeDisponivel.Should().Be(3);
        p.QuantidadeReservada.Should().Be(0);
        _reservas.Verify(r => r.AdicionarAsync(It.IsAny<ReservaPeca>(), It.IsAny<CancellationToken>()), Times.Never);
        ctx.Verify(c => c.Publish(It.Is<ReservaPecasFalhou>(m => m.Motivo.Contains("insuficiente")), It.IsAny<CancellationToken>()), Times.Once);
        ctx.Verify(c => c.Publish(It.IsAny<PecasReservadas>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reservar_mesma_peca_repetida_gera_uma_unica_reserva()
    {
        var os = Guid.NewGuid();
        var p = EstoquePeca.Criar(Guid.NewGuid(), 5);
        _estoque.Setup(e => e.ObterPorPecaIdAsync(p.PecaId, It.IsAny<CancellationToken>())).ReturnsAsync(p);
        _reservas.Setup(r => r.ListarPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ReservaPeca>());
        var ctx = Ctx(new ReservarPecas(os, DateTime.UtcNow, new[] { new ItemPecaQtd(p.PecaId, 2), new ItemPecaQtd(p.PecaId, 2) }));

        await Reservar().Consume(ctx.Object);

        p.QuantidadeReservada.Should().Be(4);
        _reservas.Verify(r => r.AdicionarAsync(It.Is<ReservaPeca>(x => x.PecaId == p.PecaId && x.Quantidade == 4), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reservar_quantidade_nao_positiva_publica_falha_sem_reservar()
    {
        var os = Guid.NewGuid();
        var p = EstoquePeca.Criar(Guid.NewGuid(), 5);
        _estoque.Setup(e => e.ObterPorPecaIdAsync(p.PecaId, It.IsAny<CancellationToken>())).ReturnsAsync(p);
        _reservas.Setup(r => r.ListarPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ReservaPeca>());
        var ctx = Ctx(new ReservarPecas(os, DateTime.UtcNow, new[] { new ItemPecaQtd(p.PecaId, 1), new ItemPecaQtd(p.PecaId, 0) }));

        await Reservar().Consume(ctx.Object);

        p.QuantidadeReservada.Should().Be(0);
        _reservas.Verify(r => r.AdicionarAsync(It.IsAny<ReservaPeca>(), It.IsAny<CancellationToken>()), Times.Never);
        ctx.Verify(c => c.Publish(It.Is<ReservaPecasFalhou>(m => m.OrdemServicoId == os && m.Motivo.Contains("positiva")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reservar_duplicado_so_republica_sucesso()
    {
        var os = Guid.NewGuid();
        _reservas.Setup(r => r.ListarPorOsAsync(os, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReservaPeca> { ReservaPeca.Criar(os, Guid.NewGuid(), 1) });
        var ctx = Ctx(new ReservarPecas(os, DateTime.UtcNow, new[] { new ItemPecaQtd(Guid.NewGuid(), 1) }));
        var ordem = new List<string>();
        ctx.Setup(c => c.Publish(It.IsAny<PecasReservadas>(), It.IsAny<CancellationToken>()))
            .Callback(() => ordem.Add("publicar")).Returns(Task.CompletedTask);
        _uow.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
            .Callback(() => ordem.Add("commit")).ReturnsAsync(1);

        await Reservar().Consume(ctx.Object);

        _estoque.Verify(e => e.ObterPorPecaIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        ordem.Should().Equal("publicar", "commit");
    }

    [Fact]
    public async Task Reservar_duplicado_apos_liberacao_nao_reserva_nem_republica()
    {
        var os = Guid.NewGuid();
        var liberada = ReservaPeca.Criar(os, Guid.NewGuid(), 1);
        liberada.Liberar();
        _reservas.Setup(r => r.ListarPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ReservaPeca> { liberada });
        var ctx = Ctx(new ReservarPecas(os, DateTime.UtcNow, new[] { new ItemPecaQtd(Guid.NewGuid(), 1) }));

        await Reservar().Consume(ctx.Object);

        _reservas.Verify(r => r.AdicionarAsync(It.IsAny<ReservaPeca>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        ctx.Verify(c => c.Publish(It.IsAny<PecasReservadas>(), It.IsAny<CancellationToken>()), Times.Never);
        ctx.Verify(c => c.Publish(It.IsAny<ReservaPecasFalhou>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reservar_sem_pecas_publica_sucesso_direto()
    {
        var os = Guid.NewGuid();
        _reservas.Setup(r => r.ListarPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ReservaPeca>());
        var ctx = Ctx(new ReservarPecas(os, DateTime.UtcNow, Array.Empty<ItemPecaQtd>()));
        var ordem = new List<string>();
        ctx.Setup(c => c.Publish(It.IsAny<PecasReservadas>(), It.IsAny<CancellationToken>()))
            .Callback(() => ordem.Add("publicar")).Returns(Task.CompletedTask);
        _uow.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
            .Callback(() => ordem.Add("commit")).ReturnsAsync(1);

        await Reservar().Consume(ctx.Object);

        ordem.Should().Equal("publicar", "commit");
    }

    [Fact]
    public async Task Reservar_com_os_vazia_lanca_sem_publicar_nem_commitar()
    {
        var ctx = Ctx(new ReservarPecas(Guid.Empty, DateTime.UtcNow, new[] { new ItemPecaQtd(Guid.NewGuid(), 1) }));

        var act = async () => await Reservar().Consume(ctx.Object);

        await act.Should().ThrowAsync<DomainException>();
        ctx.Verify(c => c.Publish(It.IsAny<PecasReservadas>(), It.IsAny<CancellationToken>()), Times.Never);
        ctx.Verify(c => c.Publish(It.IsAny<ReservaPecasFalhou>(), It.IsAny<CancellationToken>()), Times.Never);
        _liberacoes.Verify(l => l.ExisteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reservar_falha_ao_persistir_nao_vira_ReservaPecasFalhou()
    {
        var os = Guid.NewGuid();
        var p = EstoquePeca.Criar(Guid.NewGuid(), 5);
        _estoque.Setup(e => e.ObterPorPecaIdAsync(p.PecaId, It.IsAny<CancellationToken>())).ReturnsAsync(p);
        _reservas.Setup(r => r.ListarPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ReservaPeca>());
        _reservas.Setup(r => r.AdicionarAsync(It.IsAny<ReservaPeca>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("falha de persistência"));
        var ctx = Ctx(new ReservarPecas(os, DateTime.UtcNow, new[] { new ItemPecaQtd(p.PecaId, 1) }));

        var act = async () => await Reservar().Consume(ctx.Object);

        // A mutação já aconteceu: o catch de validação não pode engolir o erro como "sem peças".
        await act.Should().ThrowAsync<DomainException>().WithMessage("falha de persistência");
        ctx.Verify(c => c.Publish(It.IsAny<ReservaPecasFalhou>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reservar_com_pecas_nulas_trata_como_vazio()
    {
        var os = Guid.NewGuid();
        _reservas.Setup(r => r.ListarPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ReservaPeca>());
        var ctx = Ctx(new ReservarPecas(os, DateTime.UtcNow, null!));

        var act = async () => await Reservar().Consume(ctx.Object);

        await act.Should().NotThrowAsync();
        ctx.Verify(c => c.Publish(It.IsAny<PecasReservadas>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reservar_apos_liberacao_falha_sem_reservar()
    {
        var os = Guid.NewGuid();
        _liberacoes.Setup(l => l.ExisteAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var est = EstoquePeca.Criar(Guid.NewGuid(), 5);
        _estoque.Setup(e => e.ObterPorPecaIdAsync(est.PecaId, It.IsAny<CancellationToken>())).ReturnsAsync(est);
        _reservas.Setup(r => r.ListarPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ReservaPeca>());
        var ctx = Ctx(new ReservarPecas(os, DateTime.UtcNow, new[] { new ItemPecaQtd(est.PecaId, 2) }));

        await Reservar().Consume(ctx.Object);

        est.QuantidadeDisponivel.Should().Be(5);
        est.QuantidadeReservada.Should().Be(0);
        _reservas.Verify(r => r.AdicionarAsync(It.IsAny<ReservaPeca>(), It.IsAny<CancellationToken>()), Times.Never);
        ctx.Verify(c => c.Publish(It.Is<ReservaPecasFalhou>(m => m.OrdemServicoId == os && m.Motivo.Contains("liberada")), It.IsAny<CancellationToken>()), Times.Once);
        ctx.Verify(c => c.Publish(It.IsAny<PecasReservadas>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Liberar_valida_todos_os_estoques_antes_de_marcar_liberacao()
    {
        var os = Guid.NewGuid();
        var ok = EstoquePeca.Criar(Guid.NewGuid(), 5);
        ok.Reservar(2, "x");
        var comEstoque = ReservaPeca.Criar(os, ok.PecaId, 2);
        var semEstoque = ReservaPeca.Criar(os, Guid.NewGuid(), 1);
        _reservas.Setup(r => r.ListarPorOsAsync(os, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReservaPeca> { comEstoque, semEstoque });
        _estoque.Setup(e => e.ObterPorPecaIdAsync(ok.PecaId, It.IsAny<CancellationToken>())).ReturnsAsync(ok);
        var ctx = Ctx(new LiberarPecas(os, DateTime.UtcNow, "cancelada"));

        var act = async () => await Liberar().Consume(ctx.Object);

        await act.Should().ThrowAsync<DomainException>();
        // 1ª passada só leitura: nada estornado e nenhum marcador gravado.
        comEstoque.Status.Should().Be(StatusReserva.Ativa);
        ok.QuantidadeReservada.Should().Be(2);
        _liberacoes.Verify(l => l.AdicionarAsync(It.IsAny<LiberacaoOs>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        ctx.Verify(c => c.Publish(It.IsAny<PecasLiberadas>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Liberar_grava_marcador_depois_de_validar_os_estoques()
    {
        var os = Guid.NewGuid();
        var est = EstoquePeca.Criar(Guid.NewGuid(), 5);
        est.Reservar(2, "x");
        _reservas.Setup(r => r.ListarPorOsAsync(os, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReservaPeca> { ReservaPeca.Criar(os, est.PecaId, 2) });
        var ordem = new List<string>();
        _estoque.Setup(e => e.ObterPorPecaIdAsync(est.PecaId, It.IsAny<CancellationToken>()))
            .Callback(() => ordem.Add("validar")).ReturnsAsync(est);
        _liberacoes.Setup(l => l.AdicionarAsync(It.IsAny<LiberacaoOs>(), It.IsAny<CancellationToken>()))
            .Callback(() => ordem.Add("marcador")).Returns(Task.CompletedTask);
        var ctx = Ctx(new LiberarPecas(os, DateTime.UtcNow, "cancelada"));

        await Liberar().Consume(ctx.Object);

        ordem.Should().Equal("validar", "marcador");
        _liberacoes.Verify(l => l.AdicionarAsync(It.Is<LiberacaoOs>(x => x.OrdemServicoId == os), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
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

        await Liberar().Consume(ctx.Object);

        ativa.Status.Should().Be(StatusReserva.Liberada);
        consumida.Status.Should().Be(StatusReserva.Consumida);
        est.QuantidadeReservada.Should().Be(0);
        exec.Status.Should().Be(StatusExecucao.Falhou);
        _liberacoes.Verify(l => l.AdicionarAsync(It.Is<LiberacaoOs>(x => x.OrdemServicoId == os), It.IsAny<CancellationToken>()), Times.Once);
        ctx.Verify(c => c.Publish(It.Is<PecasLiberadas>(m => m.OrdemServicoId == os), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Liberar_sem_reserva_nem_execucao_ainda_confirma_e_marca()
    {
        var os = Guid.NewGuid();
        _reservas.Setup(r => r.ListarPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ReservaPeca>());
        var ctx = Ctx(new LiberarPecas(os, DateTime.UtcNow, "x"));

        await Liberar().Consume(ctx.Object);

        _liberacoes.Verify(l => l.AdicionarAsync(It.Is<LiberacaoOs>(x => x.OrdemServicoId == os), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        ctx.Verify(c => c.Publish(It.IsAny<PecasLiberadas>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Liberar_com_marcador_existente_nao_duplica_marcador()
    {
        var os = Guid.NewGuid();
        var est = EstoquePeca.Criar(Guid.NewGuid(), 5);
        est.Reservar(2, "x");
        var ativa = ReservaPeca.Criar(os, est.PecaId, 2);
        _reservas.Setup(r => r.ListarPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ReservaPeca> { ativa });
        _estoque.Setup(e => e.ObterPorPecaIdAsync(est.PecaId, It.IsAny<CancellationToken>())).ReturnsAsync(est);
        _liberacoes.Setup(l => l.ExisteAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var ctx = Ctx(new LiberarPecas(os, DateTime.UtcNow, "cancelada"));

        await Liberar().Consume(ctx.Object);

        _liberacoes.Verify(l => l.AdicionarAsync(It.IsAny<LiberacaoOs>(), It.IsAny<CancellationToken>()), Times.Never);
        ativa.Status.Should().Be(StatusReserva.Liberada);
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
        var consumer = Liberar();
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
    public async Task Liberar_sem_estoque_lanca_domain_exception_sem_liberar()
    {
        var os = Guid.NewGuid();
        var ativa = ReservaPeca.Criar(os, Guid.NewGuid(), 2);
        _reservas.Setup(r => r.ListarPorOsAsync(os, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ReservaPeca> { ativa });
        var ctx = Ctx(new LiberarPecas(os, DateTime.UtcNow, "cancelada"));

        var act = async () => await Liberar().Consume(ctx.Object);

        await act.Should().ThrowAsync<DomainException>();
        ativa.Status.Should().Be(StatusReserva.Ativa);
        _liberacoes.Verify(l => l.AdicionarAsync(It.IsAny<LiberacaoOs>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        ctx.Verify(c => c.Publish(It.IsAny<PecasLiberadas>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Enfileirar_cria_execucao_uma_vez()
    {
        var os = Guid.NewGuid();
        var ctx = Ctx(new EnfileirarExecucao(os, DateTime.UtcNow, new[] { new ItemServicoQtd(Guid.NewGuid(), 1) }, Array.Empty<ItemPecaQtd>()));
        await Enfileirar().Consume(ctx.Object);
        _execucoes.Verify(e => e.AdicionarAsync(It.Is<ExecucaoOS>(x => x.OrdemServicoId == os), It.IsAny<CancellationToken>()), Times.Once);

        _execucoes.Setup(e => e.ObterPorOsAsync(os, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExecucaoOS.Enfileirar(os, Array.Empty<(Guid, int)>()));
        await Enfileirar().Consume(ctx.Object);
        _execucoes.Verify(e => e.AdicionarAsync(It.IsAny<ExecucaoOS>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Enfileirar_com_servicos_nulos_cria_execucao_vazia()
    {
        var os = Guid.NewGuid();
        var ctx = Ctx(new EnfileirarExecucao(os, DateTime.UtcNow, null!, null!));

        var act = async () => await Enfileirar().Consume(ctx.Object);

        await act.Should().NotThrowAsync();
        _execucoes.Verify(e => e.AdicionarAsync(It.Is<ExecucaoOS>(x => x.OrdemServicoId == os && x.Servicos.Count == 0), It.IsAny<CancellationToken>()), Times.Once);
    }
}
