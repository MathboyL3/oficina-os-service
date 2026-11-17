using Os.Application.Auth;
using Os.Application.Common;
using Os.Application.DTOs;
using Os.Application.Mapping;
using Os.Domain.Common;
using Os.Domain.Identidade;

namespace Os.Application.UseCases.Auth;

public sealed class AuthUseCases
{
    private readonly IUsuarioRepository _repo;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _jwt;
    private const long ChaveLockBootstrap = 0x4F53_B007_57AAL;

    private readonly IUnitOfWork _uow;

    public AuthUseCases(IUsuarioRepository repo, IPasswordHasher hasher, IJwtTokenService jwt, IUnitOfWork uow)
    {
        _repo = repo;
        _hasher = hasher;
        _jwt = jwt;
        _uow = uow;
    }

    public async Task<UsuarioResponse> CriarAsync(CriarUsuarioRequest req, CancellationToken ct = default)
    {
        var existente = await _repo.ObterPorEmailAsync(req.Email.Trim().ToLowerInvariant(), ct);
        if (existente is not null)
            throw new ConflictException($"Já existe usuário com e-mail {req.Email}.");

        if (!Enum.TryParse<Role>(req.Role, true, out var role))
            throw new AppException($"Role inválida: {req.Role}. Valores válidos: Atendente, Mecanico, Gerente.");

        if (string.IsNullOrWhiteSpace(req.Senha) || req.Senha.Length < 8)
            throw new AppException("Senha deve ter ao menos 8 caracteres.");

        var hash = _hasher.Hash(req.Senha);
        var usuario = Usuario.Criar(req.Nome, req.Email, hash, role);
        await _repo.AdicionarAsync(usuario, ct);
        await _uow.CommitAsync(ct);
        return usuario.ToResponse();
    }

    /// <summary>Cria o primeiro usuário (sempre Gerente, ignorando o role do request). 409 se já existe algum usuário.</summary>
    /// <remarks>Checagem + inserção serializadas por advisory lock: duas réplicas não criam dois "primeiros" usuários.</remarks>
    public Task<UsuarioResponse> BootstrapAsync(CriarUsuarioRequest req, CancellationToken ct = default) =>
        _uow.ExecutarSerializadoAsync(ChaveLockBootstrap, async token =>
        {
            if (await _repo.ExisteAlgumAsync(token))
                throw new ConflictException("Bootstrap já realizado.");

            return await CriarAsync(req with { Role = nameof(Role.Gerente) }, token);
        }, ct);

    public async Task<TokenResponse> LoginAsync(LoginRequest req, CancellationToken ct = default)
    {
        var usuario = await _repo.ObterPorEmailAsync(req.Email.Trim().ToLowerInvariant(), ct);
        if (usuario is null || !_hasher.Verify(req.Senha, usuario.SenhaHash))
            throw new AppException("Credenciais inválidas.");

        var (token, expiresIn) = _jwt.Generate(usuario.Id, usuario.Email, usuario.Role.ToString());
        return new TokenResponse(token, "Bearer", expiresIn);
    }
}
