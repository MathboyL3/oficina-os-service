using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Os.Api.Security;
using Os.Application.UseCases.Clientes;
using Os.Domain.Common;
using Os.Domain.ValueObjects;

namespace Os.Api.Controllers;

public sealed record ClienteInternoResponse(Guid Id, string Nome, string Email);

/// <summary>Consulta interna para a fiap-auth-lambda (sem acesso direto ao banco deste serviço).</summary>
[ApiController]
[AllowAnonymous]
[InternalServiceKey]
[Route("internal/clientes")]
public class InternalClientesController : ControllerBase
{
    private readonly ClienteUseCases _uc;
    private readonly ILogger<InternalClientesController> _log;

    public InternalClientesController(ClienteUseCases uc, ILogger<InternalClientesController> log)
    {
        _uc = uc;
        _log = log;
    }

    [HttpGet("por-documento/{cpf}")]
    public async Task<ActionResult<ClienteInternoResponse>> PorDocumento(string cpf, CancellationToken ct)
    {
        Cpf valido;
        try
        {
            valido = Cpf.Create(cpf);
        }
        catch (DomainException ex)
        {
            return BadRequest(new { status = 400, message = ex.Message });
        }

        var mascarado = "***.***.***-" + valido.Numero[9..];
        var c = await _uc.BuscarPorDocumentoAsync(valido.Numero, ct);
        if (c is null)
        {
            _log.LogInformation("Cliente não encontrado para o CPF {Cpf}", mascarado);
            return NotFound(new { status = 404, message = "Cliente não encontrado." });
        }

        _log.LogInformation("Cliente encontrado para o CPF {Cpf}", mascarado);
        return Ok(new ClienteInternoResponse(c.Id, c.Nome, c.Email));
    }
}