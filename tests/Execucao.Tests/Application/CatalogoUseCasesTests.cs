using Execucao.Application.Common;
using Execucao.Application.DTOs;
using Execucao.Application.UseCases;
using Execucao.Domain.Catalogo;
using Execucao.Domain.Common;
using Execucao.Domain.Estoque;
using Execucao.Domain.ValueObjects;
using FluentAssertions;
using Moq;

namespace Execucao.Tests.Application;

public class CatalogoUseCasesTests
{
    private readonly Mock<IServicoRepository> _servicos = new();
    private readonly Mock<IPecaRepository> _pecas = new();
    private readonly Mock<IEstoquePecaRepository> _estoque = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private ServicoUseCases ServicoUc() => new(_servicos.Object, _uow.Object);
    private PecaUseCases PecaUc() => new(_pecas.Object, _estoque.Object, _uow.Object);

    [Fact]
    public async Task Cadastrar_servico_persiste_e_retorna_response()
    {
        var r = await ServicoUc().CadastrarAsync(new CadastrarServicoRequest("Alinhamento", "Geometria", 150m, 60));

        r.Nome.Should().Be("Alinhamento");
        r.ValorBase.Should().Be(150m);
        r.TempoEstimadoMinutos.Should().Be(60);
        r.Ativo.Should().BeTrue();
        _servicos.Verify(s => s.AdicionarAsync(It.Is<Servico>(x => x.Id == r.Id), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cadastrar_peca_cria_estoque_junto()
    {
        var r = await PecaUc().CadastrarAsync(new CadastrarPecaRequest("Filtro de óleo", "fil-1", "un", 30m, 5, 2));

        r.Codigo.Should().Be("FIL-1");
        r.QuantidadeDisponivel.Should().Be(5);
        r.LimiteMinimo.Should().Be(2);
        _estoque.Verify(e => e.AdicionarAsync(It.Is<EstoquePeca>(x => x.PecaId == r.Id && x.QuantidadeDisponivel == 5), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Peca_inexistente_lanca_NotFound()
        => await FluentActions.Awaiting(() => PecaUc().ObterAsync(Guid.NewGuid())).Should().ThrowAsync<NotFoundException>();

    [Fact]
    public async Task Servico_inexistente_lanca_NotFound()
        => await FluentActions.Awaiting(() => ServicoUc().ObterAsync(Guid.NewGuid())).Should().ThrowAsync<NotFoundException>();

    [Fact]
    public async Task Listar_servicos_retorna_responses()
    {
        var s = Servico.Cadastrar("Alinhamento", "Geometria", Money.Brl(150m), TimeSpan.FromMinutes(60));
        _servicos.Setup(r => r.ListarAsync(false, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Servico> { s });

        var r = await ServicoUc().ListarAsync(false);

        r.Should().ContainSingle(x => x.Id == s.Id && x.Nome == "Alinhamento" && x.ValorBase == 150m && x.TempoEstimadoMinutos == 60);
        _servicos.Verify(x => x.ListarAsync(false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Atualizar_servico_aplica_so_os_campos_informados()
    {
        var s = Servico.Cadastrar("Alinhamento", "Geometria", Money.Brl(150m), TimeSpan.FromMinutes(60));
        _servicos.Setup(r => r.ObterPorIdAsync(s.Id, It.IsAny<CancellationToken>())).ReturnsAsync(s);

        var r = await ServicoUc().AtualizarAsync(s.Id, new AtualizarServicoRequest("Alinhamento 3D", null, 200m, null));

        r.Nome.Should().Be("Alinhamento 3D");
        r.ValorBase.Should().Be(200m);
        r.Descricao.Should().Be("Geometria");
        r.TempoEstimadoMinutos.Should().Be(60);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Inativar_servico_marca_inativo_e_confirma()
    {
        var s = Servico.Cadastrar("Alinhamento", "Geometria", Money.Brl(150m), TimeSpan.FromMinutes(60));
        _servicos.Setup(r => r.ObterPorIdAsync(s.Id, It.IsAny<CancellationToken>())).ReturnsAsync(s);

        await ServicoUc().InativarAsync(s.Id);

        s.Ativo.Should().BeFalse();
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Listar_pecas_inclui_estoque_de_cada_peca()
    {
        var p1 = Peca.Cadastrar("Filtro de óleo", "fil-1", "un", Money.Brl(30m));
        var p2 = Peca.Cadastrar("Vela", "vel-1", "un", Money.Brl(10m));
        var est = EstoquePeca.Criar(p1.Id, 4, 2);
        _pecas.Setup(r => r.ListarAsync(true, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Peca> { p1, p2 });
        _estoque.Setup(e => e.ObterPorPecaIdAsync(p1.Id, It.IsAny<CancellationToken>())).ReturnsAsync(est);

        var r = await PecaUc().ListarAsync(true);

        r.Should().HaveCount(2);
        r[0].QuantidadeDisponivel.Should().Be(4);
        r[0].LimiteMinimo.Should().Be(2);
        r[1].QuantidadeDisponivel.Should().Be(0);
        _pecas.Verify(x => x.ListarAsync(true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Atualizar_valor_da_peca_persiste_e_retorna_estoque()
    {
        var p = Peca.Cadastrar("Filtro de óleo", "fil-1", "un", Money.Brl(30m));
        _pecas.Setup(r => r.ObterPorIdAsync(p.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p);
        _estoque.Setup(e => e.ObterPorPecaIdAsync(p.Id, It.IsAny<CancellationToken>())).ReturnsAsync(EstoquePeca.Criar(p.Id, 3));

        var r = await PecaUc().AtualizarValorAsync(p.Id, new AtualizarValorPecaRequest(45.5m));

        r.Valor.Should().Be(45.5m);
        p.Valor.Amount.Should().Be(45.5m);
        r.QuantidadeDisponivel.Should().Be(3);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reabastecer_peca_soma_no_estoque_e_registra_entrada()
    {
        var p = Peca.Cadastrar("Filtro de óleo", "fil-1", "un", Money.Brl(30m));
        var est = EstoquePeca.Criar(p.Id, 2);
        _pecas.Setup(r => r.ObterPorIdAsync(p.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p);
        _estoque.Setup(e => e.ObterPorPecaIdAsync(p.Id, It.IsAny<CancellationToken>())).ReturnsAsync(est);

        var r = await PecaUc().ReabastecerAsync(p.Id, new ReabastecerEstoqueRequest(5, "NF 1"));

        r.QuantidadeDisponivel.Should().Be(7);
        est.Movimentacoes.Should().Contain(m => m.Referencia == "NF 1" && m.Tipo == TipoMovimentacao.Entrada && m.Quantidade == 5);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reabastecer_peca_sem_estoque_lanca_NotFound()
    {
        var p = Peca.Cadastrar("Filtro de óleo", "fil-1", "un", Money.Brl(30m));
        _pecas.Setup(r => r.ObterPorIdAsync(p.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p);

        await FluentActions.Awaiting(() => PecaUc().ReabastecerAsync(p.Id, new ReabastecerEstoqueRequest(5, null)))
            .Should().ThrowAsync<NotFoundException>();
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Inativar_peca_marca_inativa_e_confirma()
    {
        var p = Peca.Cadastrar("Filtro de óleo", "fil-1", "un", Money.Brl(30m));
        _pecas.Setup(r => r.ObterPorIdAsync(p.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p);

        await PecaUc().InativarAsync(p.Id);

        p.Ativo.Should().BeFalse();
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
