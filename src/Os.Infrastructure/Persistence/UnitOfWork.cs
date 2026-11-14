using Microsoft.EntityFrameworkCore;
using Os.Domain.Common;

namespace Os.Infrastructure.Persistence;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly OsDbContext _ctx;
    public UnitOfWork(OsDbContext ctx) => _ctx = ctx;
    public Task<int> CommitAsync(CancellationToken ct = default) => _ctx.SaveChangesAsync(ct);

    public async Task<T> ExecutarSerializadoAsync<T>(long chave, Func<CancellationToken, Task<T>> acao, CancellationToken ct = default)
    {
        // InMemory (testes) não tem transação nem advisory lock: executa direto.
        if (!_ctx.Database.IsRelational())
            return await acao(ct);

        await using var tx = await _ctx.Database.BeginTransactionAsync(ct);
        await _ctx.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({chave})", ct);
        var resultado = await acao(ct);
        await tx.CommitAsync(ct);
        return resultado;
    }
}