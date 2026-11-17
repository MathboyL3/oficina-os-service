using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Os.Application.DTOs;
using Os.Application.UseCases.OrdensServico;
using Os.Domain.OrdensServico;

namespace Os.Api.Controllers;

[ApiController]
[Authorize(Roles = "Atendente,Gerente,Mecanico")]
[Route("api/ordens-servico")]
public class OrdensServicoController : ControllerBase
{
    private readonly OrdemServicoUseCases _uc;
    public OrdensServicoController(OrdemServicoUseCases uc) => _uc = uc;

    [HttpPost]
    public async Task<ActionResult<OSResponse>> Abrir(AbrirOSRequest req, CancellationToken ct)
    {
        var os = await _uc.AbrirAsync(req, ct);
        return CreatedAtAction(nameof(Obter), new { id = os.Id }, os);
    }

    /// <summary>Detalhe da OS, com o estado da Saga (<c>saga.estado</c>) quando existir.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OSResponse>> Obter(Guid id, CancellationToken ct)
        => Ok(await _uc.ObterAsync(id, ct));

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OSResumoResponse>>> Listar(
        [FromQuery] int skip = 0, [FromQuery] int take = 20, [FromQuery] StatusOS? status = null, CancellationToken ct = default)
        => Ok(await _uc.ListarAsync(Math.Max(0, skip), Math.Clamp(take, 1, 100), status, ct));

    /// <summary>
    /// Painel de acompanhamento: OS ativas ordenadas por prioridade de status
    /// (Em execução > Aguardando pagamento > Aguardando aprovação > Em diagnóstico > Recebida), mais antigas primeiro.
    /// Não inclui OS Finalizadas, Entregues ou Canceladas.
    /// </summary>
    [HttpGet("painel")]
    public async Task<ActionResult<IReadOnlyList<OSResumoResponse>>> Painel(
        [FromQuery] int skip = 0, [FromQuery] int take = 20, CancellationToken ct = default)
        => Ok(await _uc.ListarPainelAsync(Math.Max(0, skip), Math.Clamp(take, 1, 100), ct));

    /// <summary>Substitui os itens da OS. Nome e valor vêm do catálogo (execucao-service); só id e quantidade são informados.</summary>
    [HttpPost("{id:guid}/itens")]
    public async Task<ActionResult<OSResponse>> DefinirItens(Guid id, DefinirItensRequest req, CancellationToken ct)
        => Ok(await _uc.DefinirItensAsync(id, req, ct));

    [HttpPost("{id:guid}/diagnostico")]
    [Authorize(Roles = "Mecanico,Gerente")]
    public async Task<ActionResult<OSResponse>> IniciarDiagnostico(Guid id, IniciarDiagnosticoRequest req, CancellationToken ct)
        => Ok(await _uc.IniciarDiagnosticoAsync(id, req, ct));

    /// <summary>Publica a solicitação de orçamento; a Saga conduz a aprovação, o pagamento e a execução.</summary>
    [HttpPost("{id:guid}/enviar-orcamento")]
    public async Task<ActionResult<OSResponse>> EnviarOrcamento(Guid id, CancellationToken ct)
        => Ok(await _uc.EnviarOrcamentoAsync(id, ct));

    [HttpPost("{id:guid}/entregar")]
    public async Task<ActionResult<OSResponse>> Entregar(Guid id, CancellationToken ct)
        => Ok(await _uc.EntregarAsync(id, ct));

    [HttpPost("{id:guid}/cancelar")]
    public async Task<ActionResult<OSResponse>> Cancelar(Guid id, CancelarOSRequest req, CancellationToken ct)
        => Ok(await _uc.CancelarAsync(id, req, ct));

    [HttpGet("metricas/tempo-medio")]
    [Authorize(Roles = "Gerente")]
    public async Task<ActionResult<TempoMedioExecucaoResponse>> TempoMedio(
        [FromQuery] DateTime? de = null, [FromQuery] DateTime? ate = null, CancellationToken ct = default)
        => Ok(await _uc.CalcularTempoMedioAsync(de, ate, ct));

    /// <summary>Endpoint público (sem JWT) para o cliente consultar o status da OS.</summary>
    [HttpGet("publico/{id:guid}/status")]
    [AllowAnonymous]
    public async Task<ActionResult<StatusPublicoResponse>> StatusPublico(Guid id, CancellationToken ct)
        => Ok(await _uc.ObterStatusPublicoAsync(id, ct));
}