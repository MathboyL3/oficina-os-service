using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Os.Application.DTOs;
using Os.Application.Saga;
using Os.Application.UseCases.OrdensServico;

namespace Os.Api.Controllers;

[ApiController]
[Authorize(Roles = "Gerente")]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly OrdemServicoUseCases _uc;
    public AdminController(OrdemServicoUseCases uc) => _uc = uc;

    /// <summary>Reenvia as compensações não confirmadas de uma OS em AguardandoAcaoManual.</summary>
    [HttpPost("ordens-servico/{id:guid}/reprocessar-compensacao")]
    public async Task<IActionResult> Reprocessar(Guid id, CancellationToken ct)
    {
        await _uc.ReprocessarCompensacaoAsync(id, ct);
        return Accepted();
    }

    /// <summary>Sagas no estado informado (padrão: AguardandoAcaoManual).</summary>
    [HttpGet("sagas")]
    public async Task<ActionResult<IReadOnlyList<PendenciaResponse>>> Sagas(
        [FromQuery] string estado = EstadosSaga.AguardandoAcaoManual, CancellationToken ct = default)
        => Ok(await _uc.ListarPendenciasAsync(estado, ct));
}