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
using Os.Application.Catalogo;
using Os.Application.Saga;
using Os.Infrastructure.Persistence;

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

public sealed class OsApiFactory : WebApplicationFactory<Program>
{
    public const string Secret = "segredo-de-teste-com-mais-de-32-caracteres!";
    private readonly string _db = $"os-tests-{Guid.NewGuid()}";

    public Mock<ICatalogoClient> Catalogo { get; } = new();
    public FakeSagaConsulta Sagas { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Secret", Secret);
        builder.ConfigureTestServices(s =>
        {
            s.AddDbContext<OsDbContext>(o => o.UseInMemoryDatabase(_db));
            s.RemoveAll<ICatalogoClient>();
            s.AddSingleton(Catalogo.Object);
            s.RemoveAll<ISagaConsulta>();
            s.AddSingleton<ISagaConsulta>(Sagas);
            s.AddMassTransitTestHarness();
        });
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