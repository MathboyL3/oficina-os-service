using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Os.Application.Saga;

namespace Os.Infrastructure.Saga;

public sealed class OsSagaStateMap : SagaClassMap<OsSagaState>
{
    protected override void Configure(EntityTypeBuilder<OsSagaState> e, ModelBuilder model)
    {
        e.ToTable("os_saga_state");
        e.Property(x => x.CurrentState).HasMaxLength(40);
        e.Property(x => x.ItensJson).HasColumnType("jsonb");
        e.Property(x => x.LinkPagamento).HasMaxLength(500);
        e.Property(x => x.MotivoCancelamento).HasMaxLength(500);
        e.Property(x => x.Version).IsConcurrencyToken();
        e.HasIndex(x => x.CurrentState);
    }
}