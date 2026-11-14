using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Os.Application.Auth;
using Os.Application.Common;
using Os.Application.DTOs;
using Os.Application.UseCases.Auth;
using Os.Infrastructure.Persistence;
using Os.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace Os.Tests.Persistence;

public class BootstrapConcorrenteTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        using var ctx = Novo();
        ctx.MigrateComLock();
    }

    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    private OsDbContext Novo() => new(new DbContextOptionsBuilder<OsDbContext>().UseNpgsql(_pg.GetConnectionString()).Options);

    /// <summary>Hasher lento: alarga a janela entre a checagem "existe algum usuário?" e o INSERT.</summary>
    private sealed class HasherLento : IPasswordHasher
    {
        public string Hash(string senha)
        {
            Thread.Sleep(800);
            return "hash";
        }

        public bool Verify(string senha, string hash) => true;
    }

    private static async Task<object> TentarAsync(OsDbContext ctx, string email)
    {
        var uc = new AuthUseCases(new UsuarioRepository(ctx), new HasherLento(), Mock.Of<IJwtTokenService>(), new UnitOfWork(ctx));
        try
        {
            return await uc.BootstrapAsync(new CriarUsuarioRequest("Gerente", email, "12345678", "Gerente"));
        }
        catch (ConflictException ex)
        {
            return ex;
        }
    }

    [Fact]
    public async Task Bootstrap_concorrente_cria_exatamente_um_usuario_e_o_outro_recebe_409()
    {
        using var c1 = Novo();
        using var c2 = Novo();

        var resultados = await Task.WhenAll(
            Task.Run(() => TentarAsync(c1, "a@oficina.test")),
            Task.Run(() => TentarAsync(c2, "b@oficina.test")));

        resultados.OfType<UsuarioResponse>().Should().HaveCount(1);
        resultados.OfType<ConflictException>().Should().HaveCount(1);

        using var verifica = Novo();
        (await verifica.Usuarios.CountAsync()).Should().Be(1);
    }
}
