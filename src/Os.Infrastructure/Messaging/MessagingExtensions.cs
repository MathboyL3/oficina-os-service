using System.Diagnostics.CodeAnalysis;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Os.Application.Saga;
using Os.Infrastructure.Persistence;
using Quartz;

namespace Os.Infrastructure.Messaging;

[ExcludeFromCodeCoverage]
public static class MessagingExtensions
{
    /// <summary>
    /// Saga (state machine) com repositório EF otimista, RabbitMQ, outbox transacional EF (publicações
    /// só saem após o SaveChanges, na mesma transação do agregado e da saga) e Quartz persistente
    /// como scheduler dos timeouts. O retry cobre DbUpdateConcurrencyException (xmin da OS / Version da saga).
    /// Em "Testing" usa transporte, saga e scheduler em memória.
    /// </summary>
    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration cfg, bool testing)
    {
        services.Configure<SagaTimeouts>(cfg.GetSection(SagaTimeouts.Secao));

        if (!testing)
        {
            var conn = cfg.GetConnectionString("Postgres")
                ?? throw new InvalidOperationException("ConnectionStrings:Postgres não configurada.");
            services.AddQuartz(q =>
            {
                q.SchedulerName = "oficina-os-saga";
                q.UsePersistentStore(s =>
                {
                    s.UseProperties = true;
                    s.UsePostgres(conn);
                    s.UseSystemTextJsonSerializer();
                    s.UseClustering();
                });
            });
            services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
        }

        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();

            if (testing)
            {
                x.AddDelayedMessageScheduler();
                x.AddSagaStateMachine<OrdemServicoStateMachine, OsSagaState>().InMemoryRepository();
                x.UsingInMemory((ctx, c) => { c.UseDelayedMessageScheduler(); c.ConfigureEndpoints(ctx); });
                return;
            }

            x.AddPublishMessageScheduler();
            x.AddQuartzConsumers();

            x.AddSagaStateMachine<OrdemServicoStateMachine, OsSagaState>()
                .EntityFrameworkRepository(r =>
                {
                    r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                    r.ExistingDbContext<OsDbContext>();
                    r.UsePostgres();
                });

            x.AddEntityFrameworkOutbox<OsDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
            });

            x.AddConfigureEndpointsCallback((ctx, _, ep) =>
            {
                ep.UseMessageRetry(r =>
                {
                    r.Handle<DbUpdateConcurrencyException>();
                    r.Exponential(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2));
                });
                ep.UseEntityFrameworkOutbox<OsDbContext>(ctx);
            });

            x.UsingRabbitMq((ctx, c) =>
            {
                var rabbit = RabbitMqOptions.FromConfiguration(cfg);
                c.Host(rabbit.Host, (ushort)rabbit.Port, rabbit.VirtualHost, h =>
                {
                    h.Username(rabbit.Username);
                    h.Password(rabbit.Password);
                });
                c.UsePublishMessageScheduler();
                c.ConfigureEndpoints(ctx);
            });
        });
        return services;
    }
}