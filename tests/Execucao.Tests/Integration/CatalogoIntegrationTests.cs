using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Execucao.Application.DTOs;
using FluentAssertions;

namespace Execucao.Tests.Integration;

public class CatalogoIntegrationTests : IClassFixture<ExecucaoApiFactory>
{
    private readonly ExecucaoApiFactory _f;
    public CatalogoIntegrationTests(ExecucaoApiFactory f) => _f = f;

    private HttpClient Client(string role)
    {
        var c = _f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExecucaoApiFactory.Token(role));
        return c;
    }

    [Fact]
    public async Task Servico_crud_completo_via_api()
    {
        var gerente = Client("Gerente");

        var criado = await gerente.PostAsJsonAsync("/api/servicos",
            new CadastrarServicoRequest("Alinhamento", "Geometria", 150m, 60));
        criado.StatusCode.Should().Be(HttpStatusCode.Created);
        var servico = await criado.Content.ReadFromJsonAsync<ServicoResponse>();
        servico!.Id.Should().NotBeEmpty();
        servico.ValorBase.Should().Be(150m);
        servico.Ativo.Should().BeTrue();

        (await gerente.GetFromJsonAsync<ServicoResponse>($"/api/servicos/{servico.Id}"))!.Nome.Should().Be("Alinhamento");
        (await gerente.GetFromJsonAsync<List<ServicoResponse>>("/api/servicos")).Should().Contain(s => s.Id == servico.Id);

        var atualizado = await gerente.PatchAsJsonAsync($"/api/servicos/{servico.Id}",
            new AtualizarServicoRequest("Alinhamento 3D", null, 200m, null));
        atualizado.StatusCode.Should().Be(HttpStatusCode.OK);
        (await atualizado.Content.ReadFromJsonAsync<ServicoResponse>())!.ValorBase.Should().Be(200m);

        (await gerente.PostAsync($"/api/servicos/{servico.Id}/inativar", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await gerente.GetFromJsonAsync<List<ServicoResponse>>("/api/servicos?somenteAtivos=true"))
            .Should().NotContain(s => s.Id == servico.Id);
        (await gerente.GetFromJsonAsync<List<ServicoResponse>>("/api/servicos?somenteAtivos=false"))
            .Should().Contain(s => s.Id == servico.Id);
    }

    [Fact]
    public async Task Servico_inexistente_404_e_cadastro_exige_gerente()
    {
        (await Client("Gerente").GetAsync($"/api/servicos/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Client("Mecanico").PostAsJsonAsync("/api/servicos",
            new CadastrarServicoRequest("Sem permissão", "", 10m, 10))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Client("Atendente").GetAsync("/api/servicos")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Peca_cadastro_cria_estoque_e_permite_reabastecer()
    {
        var gerente = Client("Gerente");
        var codigo = "fil-" + Guid.NewGuid().ToString("N")[..8];

        var criada = await gerente.PostAsJsonAsync("/api/pecas",
            new CadastrarPecaRequest("Filtro de óleo", codigo, "un", 30m, 5, 1));
        criada.StatusCode.Should().Be(HttpStatusCode.Created);
        var peca = await criada.Content.ReadFromJsonAsync<PecaResponse>();
        peca!.QuantidadeDisponivel.Should().Be(5);
        peca.Codigo.Should().Be(codigo.ToUpperInvariant());

        (await gerente.GetFromJsonAsync<PecaResponse>($"/api/pecas/{peca.Id}"))!.Nome.Should().Be("Filtro de óleo");
        (await gerente.GetFromJsonAsync<List<PecaResponse>>("/api/pecas")).Should().Contain(p => p.Id == peca.Id);

        var reabastecida = await Client("Atendente").PostAsJsonAsync($"/api/pecas/{peca.Id}/reabastecer",
            new ReabastecerEstoqueRequest(3, "NF 1"));
        reabastecida.StatusCode.Should().Be(HttpStatusCode.OK);
        (await reabastecida.Content.ReadFromJsonAsync<PecaResponse>())!.QuantidadeDisponivel.Should().Be(8);

        var valor = await gerente.PatchAsJsonAsync($"/api/pecas/{peca.Id}/valor", new AtualizarValorPecaRequest(45m));
        valor.StatusCode.Should().Be(HttpStatusCode.OK);
        (await valor.Content.ReadFromJsonAsync<PecaResponse>())!.Valor.Should().Be(45m);

        (await gerente.PostAsync($"/api/pecas/{peca.Id}/inativar", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await gerente.GetFromJsonAsync<List<PecaResponse>>("/api/pecas?somenteAtivas=true"))
            .Should().NotContain(p => p.Id == peca.Id);
    }

    [Fact]
    public async Task Peca_codigo_duplicado_409_e_inexistente_404()
    {
        var gerente = Client("Gerente");
        var codigo = "vel-" + Guid.NewGuid().ToString("N")[..8];
        var req = new CadastrarPecaRequest("Vela", codigo, "un", 10m, 1, 0);

        (await gerente.PostAsJsonAsync("/api/pecas", req)).StatusCode.Should().Be(HttpStatusCode.Created);
        var duplicado = await gerente.PostAsJsonAsync("/api/pecas", req);

        duplicado.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await gerente.GetAsync($"/api/pecas/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await gerente.PostAsJsonAsync($"/api/pecas/{Guid.NewGuid()}/reabastecer",
            new ReabastecerEstoqueRequest(1, null))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
