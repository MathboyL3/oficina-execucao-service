using Execucao.Application.DTOs;
using Execucao.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Execucao.Api.Controllers;

[ApiController]
[Authorize(Roles = "Mecanico,Gerente")]
[Route("api/execucoes")]
public class ExecucoesController : ControllerBase
{
    private readonly ExecucaoUseCases _uc;
    public ExecucoesController(ExecucaoUseCases uc) => _uc = uc;

    /// <summary>Fila de execução: prioridade desc, mais antigas primeiro.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ExecucaoResponse>>> Fila([FromQuery] int skip = 0, [FromQuery] int take = 20, CancellationToken ct = default)
        => Ok(await _uc.ListarFilaAsync(Math.Max(0, skip), Math.Clamp(take, 1, 100), ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ExecucaoResponse>> Obter(Guid id, CancellationToken ct) => Ok(await _uc.ObterAsync(id, ct));

    [HttpPost("{id:guid}/iniciar")]
    public async Task<ActionResult<ExecucaoResponse>> Iniciar(Guid id, CancellationToken ct) => Ok(await _uc.IniciarAsync(id, ct));

    [HttpPost("{id:guid}/diagnostico")]
    public async Task<ActionResult<ExecucaoResponse>> Diagnostico(Guid id, RegistrarDiagnosticoRequest req, CancellationToken ct)
        => Ok(await _uc.RegistrarDiagnosticoAsync(id, req, ct));

    [HttpPost("{id:guid}/finalizar")]
    public async Task<ActionResult<ExecucaoResponse>> Finalizar(Guid id, CancellationToken ct) => Ok(await _uc.FinalizarAsync(id, ct));

    [HttpPost("{id:guid}/falhar")]
    public async Task<ActionResult<ExecucaoResponse>> Falhar(Guid id, FalharExecucaoRequest req, CancellationToken ct)
        => Ok(await _uc.FalharAsync(id, req, ct));
}
