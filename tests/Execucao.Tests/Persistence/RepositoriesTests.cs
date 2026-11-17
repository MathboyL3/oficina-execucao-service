using Execucao.Domain.Catalogo;
using Execucao.Domain.Estoque;
using Execucao.Domain.Reservas;
using Execucao.Domain.ValueObjects;
using Execucao.Infrastructure.Persistence;
using Execucao.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ExecucaoOS = Execucao.Domain.Execucoes.Execucao;

namespace Execucao.Tests.Persistence;

/// <summary>Repositórios sobre o provider InMemory: cobrem as queries e o mapeamento usados pela API.</summary>
public class CatalogoRepositoriesTests
{
    private static ExecucaoDbContext Db() => new(new DbContextOptionsBuilder<ExecucaoDbContext>()
        .UseInMemoryDatabase($"catalogo-{Guid.NewGuid()}").Options);

    [Fact]
    public async Task Servico_repositorio_filtra_ativos_e_remove()
    {
        using var db = Db();
        var repo = new ServicoRepository(db);
        var uow = new UnitOfWork(db);
        var ativo = Servico.Cadastrar("Alinhamento", "Geometria", Money.Brl(150m), TimeSpan.FromMinutes(60));
        var inativo = Servico.Cadastrar("Balanceamento", "", Money.Brl(80m), TimeSpan.FromMinutes(30));
        inativo.Inativar();
        await repo.AdicionarAsync(ativo);
        await repo.AdicionarAsync(inativo);
        await uow.CommitAsync();

        (await repo.ObterPorIdAsync(ativo.Id))!.Nome.Should().Be("Alinhamento");
        (await repo.ListarAsync(false)).Select(s => s.Nome).Should().Equal("Alinhamento", "Balanceamento");
        (await repo.ListarAsync(true)).Select(s => s.Nome).Should().Equal("Alinhamento");

        await repo.RemoverAsync(inativo);
        await uow.CommitAsync();

        (await repo.ObterPorIdAsync(inativo.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Peca_repositorio_busca_por_codigo_normalizado_e_lista_por_nome()
    {
        using var db = Db();
        var repo = new PecaRepository(db);
        var velha = Peca.Cadastrar("Vela", "vel-1", "un", Money.Brl(10m));
        var filtro = Peca.Cadastrar("Filtro de óleo", "fil-1", "un", Money.Brl(30m));
        filtro.Inativar();
        await repo.AdicionarAsync(velha);
        await repo.AdicionarAsync(filtro);
        await db.SaveChangesAsync();

        (await repo.ObterPorCodigoAsync("fil-1"))!.Id.Should().Be(filtro.Id);
        (await repo.ObterPorCodigoAsync("FIL-1"))!.Id.Should().Be(filtro.Id);
        (await repo.ObterPorIdAsync(velha.Id))!.Codigo.Should().Be("VEL-1");
        (await repo.ListarAsync(false)).Select(p => p.Nome).Should().Equal("Filtro de óleo", "Vela");
        (await repo.ListarAsync(true)).Select(p => p.Nome).Should().Equal("Vela");

        await repo.RemoverAsync(velha);
        await db.SaveChangesAsync();

        (await repo.ObterPorIdAsync(velha.Id)).Should().BeNull();
    }
}

public class EstoqueRepositoryTests
{
    private static ExecucaoDbContext Db() => new(new DbContextOptionsBuilder<ExecucaoDbContext>()
        .UseInMemoryDatabase($"estoque-{Guid.NewGuid()}").Options);

    [Fact]
    public async Task Estoque_repositorio_obtem_por_id_e_por_peca_com_movimentacoes()
    {
        using var db = Db();
        var repo = new EstoquePecaRepository(db);
        var est = EstoquePeca.Criar(Guid.NewGuid(), 10, 2);
        await repo.AdicionarAsync(est);
        await db.SaveChangesAsync();

        var porId = await repo.ObterPorIdAsync(est.Id);
        var porPeca = await repo.ObterPorPecaIdAsync(est.PecaId);

        porId.Should().NotBeNull();
        porId!.Movimentacoes.Should().ContainSingle(m => m.Tipo == TipoMovimentacao.Entrada && m.Quantidade == 10);
        porPeca!.Id.Should().Be(est.Id);
    }

    [Fact]
    public async Task Estoque_repositorio_lista_abaixo_do_limite_minimo()
    {
        using var db = Db();
        var repo = new EstoquePecaRepository(db);
        var baixo = EstoquePeca.Criar(Guid.NewGuid(), 1, 2);
        var ok = EstoquePeca.Criar(Guid.NewGuid(), 10, 2);
        await repo.AdicionarAsync(baixo);
        await repo.AdicionarAsync(ok);
        await db.SaveChangesAsync();

        var lista = await repo.ListarBaixosAsync();

        lista.Should().ContainSingle(e => e.Id == baixo.Id);
    }
}

public class ReservaRepositoryTests
{
    private static ExecucaoDbContext Db() => new(new DbContextOptionsBuilder<ExecucaoDbContext>()
        .UseInMemoryDatabase($"reserva-{Guid.NewGuid()}").Options);

    [Fact]
    public async Task Reserva_repositorio_lista_so_as_reservas_da_os()
    {
        using var db = Db();
        var reservas = new ReservaRepository(db);
        var os = Guid.NewGuid();
        await reservas.AdicionarAsync(ReservaPeca.Criar(os, Guid.NewGuid(), 2));
        await reservas.AdicionarAsync(ReservaPeca.Criar(Guid.NewGuid(), Guid.NewGuid(), 1));
        await db.SaveChangesAsync();

        var lista = await reservas.ListarPorOsAsync(os);

        lista.Should().ContainSingle(r => r.OrdemServicoId == os && r.Quantidade == 2);
    }
}

public class LiberacaoOsRepositoryTests
{
    private static ExecucaoDbContext Db() => new(new DbContextOptionsBuilder<ExecucaoDbContext>()
        .UseInMemoryDatabase($"liberacao-{Guid.NewGuid()}").Options);

    [Fact]
    public async Task Liberacao_repositorio_so_existe_depois_de_adicionar()
    {
        using var db = Db();
        var repo = new LiberacaoOsRepository(db);
        var os = Guid.NewGuid();

        (await repo.ExisteAsync(os)).Should().BeFalse();

        await repo.AdicionarAsync(LiberacaoOs.Criar(os));
        await db.SaveChangesAsync();

        (await repo.ExisteAsync(os)).Should().BeTrue();
        (await repo.ExisteAsync(Guid.NewGuid())).Should().BeFalse();
    }
}

public class ExecucaoRepositoryTests
{
    private static ExecucaoDbContext Db() => new(new DbContextOptionsBuilder<ExecucaoDbContext>()
        .UseInMemoryDatabase($"execucao-{Guid.NewGuid()}").Options);

    [Fact]
    public async Task Execucao_repositorio_obtem_por_id_com_servicos_e_por_os()
    {
        using var db = Db();
        var repo = new ExecucaoRepository(db);
        var os = Guid.NewGuid();
        var e = ExecucaoOS.Enfileirar(os, new[] { (Guid.NewGuid(), 2), (Guid.NewGuid(), 1) });
        await repo.AdicionarAsync(e);
        await db.SaveChangesAsync();

        var porId = await repo.ObterPorIdAsync(e.Id);
        var porOs = await repo.ObterPorOsAsync(os);

        porId!.Servicos.Should().HaveCount(2);
        porOs!.Id.Should().Be(e.Id);
        (await repo.ObterPorOsAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task Execucao_repositorio_lista_fila_por_prioridade_e_chegada()
    {
        using var db = Db();
        var repo = new ExecucaoRepository(db);

        var finalizada = ExecucaoOS.Enfileirar(Guid.NewGuid(), Array.Empty<(Guid, int)>(), prioridade: 99);
        finalizada.Iniciar();
        finalizada.RegistrarDiagnostico("ok");
        finalizada.Finalizar();
        await repo.AdicionarAsync(finalizada);

        var urgente = ExecucaoOS.Enfileirar(Guid.NewGuid(), Array.Empty<(Guid, int)>(), prioridade: 5);
        await repo.AdicionarAsync(urgente);

        var antiga = ExecucaoOS.Enfileirar(Guid.NewGuid(), Array.Empty<(Guid, int)>());
        antiga.Iniciar();
        await repo.AdicionarAsync(antiga);
        await db.SaveChangesAsync();
        await Task.Delay(20);

        var nova = ExecucaoOS.Enfileirar(Guid.NewGuid(), Array.Empty<(Guid, int)>());
        await repo.AdicionarAsync(nova);
        await db.SaveChangesAsync();

        var fila = await repo.ListarFilaAsync(0, 20);

        fila.Select(x => x.Id).Should().Equal(urgente.Id, antiga.Id, nova.Id);
        (await repo.ListarFilaAsync(1, 1)).Select(x => x.Id).Should().Equal(antiga.Id);
    }

    [Fact]
    public async Task UnitOfWork_grava_alteracoes_pendentes()
    {
        using var db = Db();
        var uow = new UnitOfWork(db);
        var e = ExecucaoOS.Enfileirar(Guid.NewGuid(), Array.Empty<(Guid, int)>());
        await db.Execucoes.AddAsync(e);

        (await uow.CommitAsync()).Should().Be(1);
    }
}
