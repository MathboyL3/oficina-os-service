using Microsoft.EntityFrameworkCore;
using Os.Domain.Identidade;

namespace Os.Infrastructure.Persistence.Repositories;

public sealed class UsuarioRepository : IUsuarioRepository
{
    private readonly OsDbContext _ctx;
    public UsuarioRepository(OsDbContext ctx) => _ctx = ctx;

    public Task<Usuario?> ObterPorEmailAsync(string email, CancellationToken ct = default) =>
        _ctx.Usuarios.FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<Usuario?> ObterPorIdAsync(Guid id, CancellationToken ct = default) =>
        _ctx.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<bool> ExisteAlgumAsync(CancellationToken ct = default) =>
        _ctx.Usuarios.AnyAsync(ct);

    public Task AdicionarAsync(Usuario usuario, CancellationToken ct = default)
    {
        _ctx.Usuarios.Add(usuario);
        return Task.CompletedTask;
    }
}
