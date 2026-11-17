using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Os.Application.DTOs;

namespace Os.Tests.Integration;

public sealed class AuthIntegrationTests : IClassFixture<OsApiFactory>
{
    private readonly OsApiFactory _factory;
    private readonly HttpClient _client;

    public AuthIntegrationTests(OsApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // Cada teste de bootstrap usa um banco novo (o bootstrap só vale para sistema sem usuários).
    [Fact]
    public async Task Bootstrap_PrimeiroUsuario_DeveCriarGerenteMesmoComRoleAtendenteNoBody()
    {
        await using var factory = new OsApiFactory();
        var client = factory.CreateClient();
        var request = new CriarUsuarioRequest(
            "Gerente Integracao",
            $"gerente-{Guid.NewGuid():N}@oficina.test",
            "Senha123!",
            "Atendente");

        var response = await client.PostAsJsonAsync("/api/auth/bootstrap", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var usuario = await response.Content.ReadFromJsonAsync<UsuarioResponse>();
        usuario.Should().NotBeNull();
        usuario!.Email.Should().Be(request.Email.ToLowerInvariant());
        usuario.Role.Should().Be("Gerente");
    }

    [Fact]
    public async Task Bootstrap_ComUsuarioExistente_DeveRetornarConflictMesmoComOutroEmail()
    {
        await using var factory = new OsApiFactory();
        var client = factory.CreateClient();
        var primeiro = await client.PostAsJsonAsync("/api/auth/bootstrap",
            new CriarUsuarioRequest("Primeiro", $"a-{Guid.NewGuid():N}@oficina.test", "Senha123!", "Gerente"));
        primeiro.StatusCode.Should().Be(HttpStatusCode.Created);

        var segundo = await client.PostAsJsonAsync("/api/auth/bootstrap",
            new CriarUsuarioRequest("Segundo", $"b-{Guid.NewGuid():N}@oficina.test", "Senha123!", "Gerente"));

        segundo.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await segundo.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("message").GetString().Should().Be("Bootstrap já realizado.");
    }

    [Fact]
    public async Task Login_ComCredenciaisValidas_DeveRetornarTokenBearer()
    {
        var email = $"login-{Guid.NewGuid():N}@oficina.test";
        var senha = "Senha123!";

        await _factory.SemearUsuarioAsync(email, senha, Os.Domain.Identidade.Role.Gerente);

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, senha));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        token.Should().NotBeNull();
        token!.TokenType.Should().Be("Bearer");
        token.AccessToken.Should().NotBeNullOrWhiteSpace();
        token.ExpiresInSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Login_ComCredenciaisInvalidas_DeveRetornarBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest($"inexistente-{Guid.NewGuid():N}@oficina.test", "Senha123!"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
