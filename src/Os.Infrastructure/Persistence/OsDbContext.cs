using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Os.Application.Saga;
using Os.Domain.Clientes;
using Os.Domain.Identidade;
using Os.Domain.OrdensServico;
using Os.Domain.Veiculos;
using Os.Infrastructure.Saga;

namespace Os.Infrastructure.Persistence;

public class OsDbContext : SagaDbContext
{
    public OsDbContext(DbContextOptions<OsDbContext> options) : base(options) { }

    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Veiculo> Veiculos => Set<Veiculo>();
    public DbSet<OrdemDeServico> OrdensServico => Set<OrdemDeServico>();
    public DbSet<HistoricoStatus> HistoricoStatus => Set<HistoricoStatus>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<OsSagaState> Sagas => Set<OsSagaState>();

    protected override IEnumerable<ISagaClassMap> Configurations { get { yield return new OsSagaStateMap(); } }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.ApplyConfigurationsFromAssembly(typeof(OsDbContext).Assembly);
        // Tabelas do outbox transacional / inbox (idempotência) do MassTransit.
        b.AddInboxStateEntity();
        b.AddOutboxMessageEntity();
        b.AddOutboxStateEntity();
    }
}