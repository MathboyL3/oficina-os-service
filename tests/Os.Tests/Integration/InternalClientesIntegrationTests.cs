using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Os.Application.DTOs;

namespace Os.Tests.Integration;

public sealed class InternalClientesIntegrationTests : IClassFixture<OsApiFactory>
{
    private const string Cpf = "52998224725";
    private readonly OsApiFactory _factory;

    public InternalClientesIntegrationTests(OsApiFactory factory) => _factory = factory;

    private HttpRequestMessage Req(string cpf, string? chave = OsApiFactory.ServiceKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"/internal/clientes/por-documento/{cpf}");
        if (chave is not null) req.Headers.Add("X-Service-Key", chave);
        return req;
    }

    private async Task<ClienteResponse> CriarClienteAsync(string documento)
    {
        var email = $"gerente-int-{Guid.NewGuid():N}@oficina.test";
        await _factory.SemearUsuarioAsync(email, "Senha123!", Os.Domain.Identidade.Role.Gerente);
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Senha123!"));
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var resp = await client.PostAsJsonAsync("/api/clientes",
            new CriarClienteRequest("Maria Silva", documento, $"maria-{Guid.NewGuid():N}@oficina.test", "11999999999"));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ClienteResponse>())!;
    }

    [Fact]
    public async Task ComChaveCorretaEClienteExistente_DeveRetornar200ComCamelCase()
    {
        var criado = await CriarClienteAsync("11144477735");

        var resp = await _factory.CreateClient().SendAsync(Req("11144477735"));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(new[] { "id", "nome", "email" });
        root.GetProperty("id").GetGuid().Should().Be(criado.Id);
        root.GetProperty("nome").GetString().Should().Be("Maria Silva");
        root.GetProperty("email").GetString().Should().Be(criado.Email);
    }

    [Fact]
    public async Task CpfComMascara_DeveFuncionar()
    {
        await CriarClienteAsync("39053344705");

        var resp = await _factory.CreateClient().SendAsync(Req("390.533.447-05"));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ClienteInexistente_DeveRetornar404()
    {
        var resp = await _factory.CreateClient().SendAsync(Req("52998224725"));

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SemHeader_DeveRetornar401()
    {
        var resp = await _factory.CreateClient().SendAsync(Req(Cpf, chave: null));

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChaveErrada_DeveRetornar401()
    {
        var resp = await _factory.CreateClient().SendAsync(Req(Cpf, "chave-errada"));

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChaveNaoConfigurada_DeveRetornar503MesmoComHeaderVazio()
    {
        await using var factory = _factory.WithWebHostBuilder(b => b.UseSetting("InternalApi:ServiceKey", ""));

        var resp = await factory.CreateClient().SendAsync(Req(Cpf, ""));

        resp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("Serviço indisponível.").And.NotContain("ServiceKey");
    }

    [Fact]
    public async Task HeaderDuplicado_DeveRetornar401()
    {
        var req = Req(Cpf, chave: null);
        req.Headers.Add("X-Service-Key", new[] { OsApiFactory.ServiceKey, OsApiFactory.ServiceKey });

        var resp = await _factory.CreateClient().SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RequestLog_NaoDeveConterCpfEmClaro()
    {
        const string cpf = "29537995593";
        await _factory.CreateClient().SendAsync(Req(cpf));
        await _factory.CreateClient().SendAsync(Req("295.379.955-93"));

        var linhas = _factory.Logs.Linhas.Where(l => l.Contains("/internal/clientes/por-documento")).ToList();
        linhas.Should().NotBeEmpty();
        linhas.Should().OnlyContain(l => !l.Contains(cpf) && !l.Contains("295.379.955-93") && !l.Contains("295.379.955"));
        linhas.Should().OnlyContain(l => !l.Contains("RequestPath=") || l.Contains("RequestPath=\"/internal/clientes/por-documento/{valor}\""));
        linhas.Should().Contain(l => l.Contains("/internal/clientes/por-documento/{valor}"));
    }

    [Fact]
    public async Task RequestLog_ApiPorDocumento_NaoDeveConterCpfEmClaro()
    {
        const string cpf = "29537995593";
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", OsApiFactory.Token("Atendente"));
        // o middleware de request log usa o Log.Logger estático (do último host criado; as fixtures rodam em paralelo)
        Serilog.Log.Logger = _factory.Services.GetRequiredService<Serilog.ILogger>();
        await client.GetAsync($"/api/clientes/por-documento/{cpf}");

        // o request log é emitido ao fim do pipeline, possivelmente após o cliente receber a resposta
        for (var i = 0; i < 50 && !_factory.Logs.Linhas.Any(l => l.Contains("RequestPath=") && l.Contains("/api/clientes/por-documento")); i++)
            await Task.Delay(100);

        var linhas = _factory.Logs.Linhas.Where(l => l.Contains("/api/clientes/por-documento")).ToList();
        linhas.Should().NotBeEmpty();
        linhas.Should().OnlyContain(l => !l.Contains(cpf));
        linhas.Should().OnlyContain(l => !l.Contains("RequestPath=") || l.Contains("RequestPath=\"/api/clientes/por-documento/{valor}\""));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("11111111111")]
    [InlineData("52998224726")]
    public async Task CpfInvalido_DeveRetornar400(string cpf)
    {
        var resp = await _factory.CreateClient().SendAsync(Req(cpf));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}