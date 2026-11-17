using System.Text.Json;
using Execucao.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Execucao.Tests.Api;

public class ErrorHandlingMiddlewareTests
{
    private static async Task<(int Status, string Body)> Executar(Exception ex)
    {
        var ctx = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var mw = new ErrorHandlingMiddleware(_ => throw ex, NullLogger<ErrorHandlingMiddleware>.Instance);

        await mw.InvokeAsync(ctx);

        ctx.Response.Body.Position = 0;
        return (ctx.Response.StatusCode, await new StreamReader(ctx.Response.Body).ReadToEndAsync());
    }

    private static string Mensagem(string body)
        => JsonSerializer.Deserialize<JsonElement>(body).GetProperty("message").GetString()!;

    [Fact]
    public async Task Update_concorrente_vira_409()
    {
        var (status, body) = await Executar(new DbUpdateConcurrencyException("linha alterada por outra transação"));

        status.Should().Be(StatusCodes.Status409Conflict);
        Mensagem(body).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Violacao_de_unicidade_23505_vira_409()
    {
        var pg = new PostgresException("duplicate key value violates unique constraint", "ERROR", "ERROR",
            PostgresErrorCodes.UniqueViolation);

        var (status, body) = await Executar(new DbUpdateException("save", pg));

        status.Should().Be(StatusCodes.Status409Conflict);
        Mensagem(body).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task DbUpdateException_de_outra_causa_continua_500()
    {
        var (status, _) = await Executar(new DbUpdateException("save", new InvalidOperationException("outra causa")));

        status.Should().Be(StatusCodes.Status500InternalServerError);
    }
}
