using Os.Domain.Common;

namespace Os.Infrastructure.Persistence;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly OsDbContext _ctx;
    public UnitOfWork(OsDbContext ctx) => _ctx = ctx;
    public Task<int> CommitAsync(CancellationToken ct = default) => _ctx.SaveChangesAsync(ct);
}