using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Os.Infrastructure.Persistence;

/// <summary>
/// Permite ao EF Core CLI criar o DbContext em design-time (migrations) sem precisar
/// de Postgres rodando ou de configuração externa.
/// </summary>
public class OsDbContextFactory : IDesignTimeDbContextFactory<OsDbContext>
{
    public OsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OsDbContext>()
            .UseNpgsql("Host=localhost;Database=oficina_design;Username=design;Password=design")
            .Options;
        return new OsDbContext(options);
    }
}