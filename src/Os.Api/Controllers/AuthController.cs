using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Os.Application.DTOs;
using Os.Application.UseCases.Auth;

namespace Os.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthUseCases _uc;
    public AuthController(AuthUseCases uc) => _uc = uc;

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest req, CancellationToken ct)
        => Ok(await _uc.LoginAsync(req, ct));

    [HttpPost("usuarios")]
    [Authorize(Roles = "Gerente")]
    public async Task<ActionResult<UsuarioResponse>> Criar(CriarUsuarioRequest req, CancellationToken ct)
    {
        var u = await _uc.CriarAsync(req, ct);
        return CreatedAtAction(nameof(Criar), new { id = u.Id }, u);
    }

    /// <summary>
    /// Bootstrap: só cria se NÃO existir nenhum usuário; o usuário criado é sempre Gerente (o role do body é ignorado).
    /// Depois do primeiro uso responde 409 "Bootstrap já realizado".
    /// </summary>
    [HttpPost("bootstrap")]
    [AllowAnonymous]
    public async Task<ActionResult<UsuarioResponse>> Bootstrap(CriarUsuarioRequest req, CancellationToken ct)
    {
        var u = await _uc.BootstrapAsync(req, ct);
        return CreatedAtAction(nameof(Bootstrap), new { id = u.Id }, u);
    }
}
