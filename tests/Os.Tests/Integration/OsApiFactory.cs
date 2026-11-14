using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Os.Application.Auth;
using Os.Application.Catalogo;
using Os.Application.Saga;
using Os.Domain.Identidade;
using Os.Infrastructure.Persistence;
using Serilog.Core;
using Serilog.Events;

namespace Os.Tests.Integration;

/// <summary>Consulta de saga controlável pelos testes (a state machine real usa o repositório em memória do MassTransit).</summary>
public sealed class FakeSagaConsulta : ISagaConsulta
{
    private readonly Dictionary<Guid, OsSagaState> _sagas = new();

    public void Semear(Guid osId, string estado) =>
        _sagas[osId] = new OsSagaState { CorrelationId = osId, CurrentState = estado };

    public Task<OsSagaState?> ObterAsync(Guid osId, CancellationToken ct) =>
        Task.FromResult(_sagas.GetValueOrDefault(osId));

    public Task<IReadOnlyList<OsSagaState>> ListarPorEstadoAsync(string estado, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<OsSagaState>>(_sagas.Values.Where(s => s.CurrentState == estado).ToList());
}

/// <summary>Sink de teste: guarda os eventos Serilog renderizados (mensagem + propriedades) para asserções sobre o que é logado.</summary>
public sealed class ColetorDeLogs : ILogEventSink
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _linhas = new();

    public IReadOnlyCollection<string> Linhas => _linhas.ToArray();

    public void Emit(LogEvent logEvent) =>
        _linhas.Enqueue(logEvent.RenderMessage() + ' ' + string.Join(' ', logEvent.Properties.Select(p => $"{p.Key}={p.Value}")));
}

public sealed class OsApiFactory : WebApplicationFactory<Program>
{
    public const string Secret = "segredo-de-teste-com-mais-de-32-caracteres!";
    public const string ServiceKey = "chave-de-servico-de-teste-0123456789";
    private readonly string _db = $"os-tests-{Guid.NewGuid()}";

    public Mock<ICatalogoClient> Catalogo { get; } = new();
    public FakeSagaConsulta Sagas { get; } = new();
    public ColetorDeLogs Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Secret", Secret);
        builder.UseSetting("InternalApi:ServiceKey", ServiceKey);
        builder.ConfigureTestServices(s =>
        {
            s.AddDbContext<OsDbContext>(o => o.UseInMemoryDatabase(_db));
            s.RemoveAll<ICatalogoClient>();
            s.AddSingleton(Catalogo.Object);
            s.RemoveAll<ISagaConsulta>();
            s.AddSingleton<ISagaConsulta>(Sagas);
            s.AddSingleton<ILogEventSink>(Logs);
            s.AddMassTransitTestHarness();
        });
    }

    /// <summary>Semeia um usuário direto no banco (o bootstrap só funciona uma vez por banco).</summary>
    public async Task SemearUsuarioAsync(string email, string senha, Role role)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OsDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        db.Usuarios.Add(Usuario.Criar("Usuario Teste", email, hasher.Hash(senha), role));
        await db.SaveChangesAsync();
    }

    public ITestHarness Harness => Services.GetRequiredService<ITestHarness>();

    public static string Token(string role)
    {
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken("Oficina.Api", "Oficina.Api",
            new[] { new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, role) },
            DateTime.UtcNow, DateTime.UtcNow.AddMinutes(10), creds);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}