using FluentAssertions;
using Moq;
using Os.Application.Auth;
using Os.Application.Common;
using Os.Application.DTOs;
using Os.Application.UseCases.Auth;
using Os.Domain.Common;
using Os.Domain.Identidade;

namespace Os.Tests.Application;

public class AuthUseCasesTests
{
    private readonly Mock<IUsuarioRepository> _repo = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IJwtTokenService> _jwt = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private AuthUseCases UC() => new(_repo.Object, _hasher.Object, _jwt.Object, _uow.Object);

    [Fact]
    public async Task Login_ComCredenciaisValidas_DeveRetornarToken()
    {
        var u = Usuario.Criar("X", "x@y.com", "hash", Role.Atendente);
        _repo.Setup(r => r.ObterPorEmailAsync("x@y.com", default)).ReturnsAsync(u);
        _hasher.Setup(h => h.Verify("12345678", "hash")).Returns(true);
        _jwt.Setup(j => j.Generate(u.Id, u.Email, "Atendente")).Returns(("token", 3600));

        var resp = await UC().LoginAsync(new LoginRequest("x@y.com", "12345678"));

        resp.AccessToken.Should().Be("token");
        resp.TokenType.Should().Be("Bearer");
        resp.ExpiresInSeconds.Should().Be(3600);
    }

    [Fact]
    public async Task Login_UsuarioInexistente_DeveLancar()
    {
        _repo.Setup(r => r.ObterPorEmailAsync(It.IsAny<string>(), default)).ReturnsAsync((Usuario?)null);
        Func<Task> act = () => UC().LoginAsync(new LoginRequest("x@y.com", "12345678"));
        await act.Should().ThrowAsync<AppException>().WithMessage("*inválidas*");
    }

    [Fact]
    public async Task Login_SenhaErrada_DeveLancar()
    {
        var u = Usuario.Criar("X", "x@y.com", "hash", Role.Atendente);
        _repo.Setup(r => r.ObterPorEmailAsync("x@y.com", default)).ReturnsAsync(u);
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        Func<Task> act = () => UC().LoginAsync(new LoginRequest("x@y.com", "wrong123"));
        await act.Should().ThrowAsync<AppException>();
    }

    [Fact]
    public async Task Criar_ComEmailExistente_DeveLancarConflict()
    {
        var existente = Usuario.Criar("X", "x@y.com", "hash", Role.Atendente);
        _repo.Setup(r => r.ObterPorEmailAsync("x@y.com", default)).ReturnsAsync(existente);

        Func<Task> act = () => UC().CriarAsync(new CriarUsuarioRequest("Y", "x@y.com", "12345678", "Gerente"));
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Criar_ComRoleInvalida_DeveLancar()
    {
        _repo.Setup(r => r.ObterPorEmailAsync(It.IsAny<string>(), default)).ReturnsAsync((Usuario?)null);
        Func<Task> act = () => UC().CriarAsync(new CriarUsuarioRequest("X", "x@y.com", "12345678", "Hacker"));
        await act.Should().ThrowAsync<AppException>().WithMessage("*Role inválida*");
    }

    [Fact]
    public async Task Criar_ComSenhaCurta_DeveLancar()
    {
        _repo.Setup(r => r.ObterPorEmailAsync(It.IsAny<string>(), default)).ReturnsAsync((Usuario?)null);
        Func<Task> act = () => UC().CriarAsync(new CriarUsuarioRequest("X", "x@y.com", "1234", "Gerente"));
        await act.Should().ThrowAsync<AppException>().WithMessage("*8 caracteres*");
    }

    [Fact]
    public async Task Bootstrap_SemUsuarios_DeveCriarSempreGerente()
    {
        _repo.Setup(r => r.ExisteAlgumAsync(default)).ReturnsAsync(false);
        _repo.Setup(r => r.ObterPorEmailAsync(It.IsAny<string>(), default)).ReturnsAsync((Usuario?)null);
        _hasher.Setup(h => h.Hash("12345678")).Returns("hash");

        var resp = await UC().BootstrapAsync(new CriarUsuarioRequest("X", "x@y.com", "12345678", "Atendente"));

        resp.Role.Should().Be("Gerente");
        _repo.Verify(r => r.AdicionarAsync(It.Is<Usuario>(u => u.Role == Role.Gerente), default), Times.Once);
    }

    [Fact]
    public async Task Bootstrap_ComUsuarioExistente_DeveLancarConflict()
    {
        _repo.Setup(r => r.ExisteAlgumAsync(default)).ReturnsAsync(true);

        Func<Task> act = () => UC().BootstrapAsync(new CriarUsuarioRequest("X", "n@y.com", "12345678", "Gerente"));

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*Bootstrap já realizado*");
        _repo.Verify(r => r.AdicionarAsync(It.IsAny<Usuario>(), default), Times.Never);
    }
}