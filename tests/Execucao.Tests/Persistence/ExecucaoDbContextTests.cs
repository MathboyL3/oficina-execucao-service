using Execucao.Domain.Catalogo;
using Execucao.Domain.Estoque;
using Execucao.Domain.Execucoes;
using Execucao.Domain.Reservas;
using Execucao.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ExecucaoOS = Execucao.Domain.Execucoes.Execucao;

namespace Execucao.Tests.Persistence;

/// <summary>
/// Modelo construído com o provider de produção (Npgsql): resolve metadados sem abrir conexão.
/// </summary>
public class ExecucaoDbContextTests
{
    private static ExecucaoDbContext Ctx() => new ExecucaoDbContextFactory().CreateDbContext(Array.Empty<string>());

    [Theory]
    [InlineData(typeof(Servico), "servicos")]
    [InlineData(typeof(Peca), "pecas")]
    [InlineData(typeof(EstoquePeca), "estoque_peca")]
    [InlineData(typeof(MovimentacaoEstoque), "estoque_movimentacoes")]
    [InlineData(typeof(ReservaPeca), "reservas_peca")]
    [InlineData(typeof(LiberacaoOs), "liberacoes_os")]
    [InlineData(typeof(ExecucaoOS), "execucoes")]
    [InlineData(typeof(ServicoExecucao), "execucao_servicos")]
    public void Modelo_mapeia_tabelas(Type entidade, string tabela)
        => Ctx().Model.FindEntityType(entidade)!.GetTableName().Should().Be(tabela);

    [Fact]
    public void Modelo_inclui_tabelas_do_outbox_e_inbox_do_masstransit()
    {
        var tabelas = Ctx().Model.GetEntityTypes().Select(e => e.GetTableName()).ToList();

        tabelas.Should().Contain(new[] { "InboxState", "OutboxMessage", "OutboxState" });
    }

    [Fact]
    public void Estoque_peca_usa_xmin_como_token_de_concorrencia()
    {
        var xmin = Ctx().Model.FindEntityType(typeof(EstoquePeca))!.FindProperty("xmin");

        xmin.Should().NotBeNull();
        xmin!.IsConcurrencyToken.Should().BeTrue();
        xmin.ValueGenerated.Should().Be(ValueGenerated.OnAddOrUpdate);
    }

    [Fact]
    public void Execucao_tem_indice_unico_por_ordem_servico()
    {
        var indice = Ctx().Model.FindEntityType(typeof(ExecucaoOS))!.GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(ExecucaoOS.OrdemServicoId) }));

        indice.IsUnique.Should().BeTrue();
    }

    [Fact]
    public void Liberacao_tem_indice_unico_por_ordem_servico()
    {
        var indice = Ctx().Model.FindEntityType(typeof(LiberacaoOs))!.GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(LiberacaoOs.OrdemServicoId) }));

        indice.IsUnique.Should().BeTrue();
    }

    [Fact]
    public void Execucao_tem_indice_da_fila()
    {
        var indice = Ctx().Model.FindEntityType(typeof(ExecucaoOS))!.GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name)
                .SequenceEqual(new[] { nameof(ExecucaoOS.Status), nameof(ExecucaoOS.Prioridade), nameof(ExecucaoOS.EnfileiradaEm) }));

        indice.IsUnique.Should().BeFalse();
    }
}
