using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Execucao.Application.DTOs;
using Execucao.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ExecucaoOS = Execucao.Domain.Execucoes.Execucao;

namespace Execucao.Tests.Integration;

public class ExecucoesIntegrationTests : IClassFixture<ExecucaoApiFactory>
{
    private readonly ExecucaoApiFactory _f;
    public ExecucoesIntegrationTests(ExecucaoApiFactory f) => _f = f;

    private HttpClient Client(string role)
    {
        var c = _f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExecucaoApiFactory.Token(role));
        return c;
    }

    private async Task<ExecucaoOS> EnfileirarAsync()
    {
        var exec = ExecucaoOS.Enfileirar(Guid.NewGuid(), Array.Empty<(Guid, int)>());
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExecucaoDbContext>();
        db.Execucoes.Add(exec);
        await db.SaveChangesAsync();
        return exec;
    }

    [Fact]
    public async Task Sem_token_401_e_atendente_403()
    {
        (await _f.CreateClient().GetAsync("/api/execucoes")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Client("Atendente").GetAsync("/api/execucoes")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Mecanico_inicia_execucao_da_fila()
    {
        var exec = await EnfileirarAsync();
        var c = Client("Mecanico");

        (await c.GetFromJsonAsync<List<ExecucaoResponse>>("/api/execucoes")).Should().NotBeEmpty();
        (await c.PostAsync($"/api/execucoes/{exec.Id}/iniciar", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.PostAsync($"/api/execucoes/{exec.Id}/finalizar", null)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await c.PostAsync($"/api/execucoes/{Guid.NewGuid()}/iniciar", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Fluxo_completo_de_execucao_responde_os_estados()
    {
        var exec = await EnfileirarAsync();
        var c = Client("Gerente");

        (await c.PostAsync($"/api/execucoes/{exec.Id}/iniciar", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var diagnostico = await c.PostAsJsonAsync($"/api/execucoes/{exec.Id}/diagnostico", new RegistrarDiagnosticoRequest("freio gasto"));
        diagnostico.StatusCode.Should().Be(HttpStatusCode.OK);
        (await diagnostico.Content.ReadFromJsonAsync<ExecucaoResponse>())!.ObservacoesDiagnostico.Should().Be("freio gasto");

        var finalizada = await c.PostAsync($"/api/execucoes/{exec.Id}/finalizar", null);
        finalizada.StatusCode.Should().Be(HttpStatusCode.OK);
        (await finalizada.Content.ReadFromJsonAsync<ExecucaoResponse>())!.Status.Should().Be("Finalizada");

        (await c.GetFromJsonAsync<ExecucaoResponse>($"/api/execucoes/{exec.Id}"))!.Status.Should().Be("Finalizada");
    }

    [Fact]
    public async Task Falhar_execucao_marca_motivo()
    {
        var exec = await EnfileirarAsync();
        var c = Client("Mecanico");

        var r = await c.PostAsJsonAsync($"/api/execucoes/{exec.Id}/falhar", new FalharExecucaoRequest("motor fundido"));

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await r.Content.ReadFromJsonAsync<ExecucaoResponse>();
        body!.Status.Should().Be("Falhou");
        body.MotivoFalha.Should().Be("motor fundido");
    }

    [Fact]
    public async Task Validacoes_e_inexistentes_mapeiam_para_422_e_404()
    {
        var exec = await EnfileirarAsync();
        var c = Client("Mecanico");

        (await c.PostAsJsonAsync($"/api/execucoes/{exec.Id}/diagnostico", new RegistrarDiagnosticoRequest(""))).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);
        (await c.PostAsJsonAsync($"/api/execucoes/{exec.Id}/falhar", new FalharExecucaoRequest(""))).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);
        (await c.GetAsync($"/api/execucoes/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsync($"/api/execucoes/{Guid.NewGuid()}/finalizar", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Health_live_ok()
        => (await _f.CreateClient().GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);

    [Fact]
    public async Task Swagger_na_raiz_expoe_o_documento_v1()
    {
        var doc = await _f.CreateClient().GetAsync("/swagger/v1/swagger.json");

        doc.StatusCode.Should().Be(HttpStatusCode.OK);
        (await doc.Content.ReadAsStringAsync()).Should().Contain("/api/execucoes");
    }
}
