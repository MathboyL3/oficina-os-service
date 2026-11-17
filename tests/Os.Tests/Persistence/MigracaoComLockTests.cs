using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Os.Infrastructure.Persistence;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Os.Tests.Persistence;

public class MigracaoComLockTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public Task InitializeAsync() => _pg.StartAsync();
    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    private OsDbContext Novo() => new(new DbContextOptionsBuilder<OsDbContext>().UseNpgsql(_pg.GetConnectionString()).Options);

    [Fact]
    public async Task Migracoes_concorrentes_nao_falham_e_aplicam_o_schema()
    {
        var tarefas = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            using var ctx = Novo();
            ctx.MigrateComLock();
        }));

        await Task.WhenAll(tarefas);

        using var verifica = Novo();
        verifica.Database.GetPendingMigrations().Should().BeEmpty();
        verifica.Database.GetAppliedMigrations().Should().NotBeEmpty();
    }

    [Fact]
    public void Lock_e_liberado_apos_migrar()
    {
        using (var ctx = Novo()) ctx.MigrateComLock();

        using var conn = new NpgsqlConnection(_pg.GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT pg_try_advisory_lock({MigracaoComLock.ChaveLock})";
        ((bool)cmd.ExecuteScalar()!).Should().BeTrue();
    }
}