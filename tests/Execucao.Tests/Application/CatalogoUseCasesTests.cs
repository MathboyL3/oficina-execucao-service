using Execucao.Application.Common;
using Execucao.Application.DTOs;
using Execucao.Application.UseCases;
using Execucao.Domain.Catalogo;
using Execucao.Domain.Common;
using Execucao.Domain.Estoque;
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
}
