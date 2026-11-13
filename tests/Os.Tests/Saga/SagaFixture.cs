using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Oficina.Contracts;
using Os.Application.Saga;

namespace Os.Tests.Saga;

public sealed class SagaFixture : IAsyncDisposable
{
    public ServiceProvider Provider { get; }
    public ITestHarness Harness { get; }
    public ISagaStateMachineTestHarness<OrdemServicoStateMachine, OsSagaState> Saga { get; }
    public List<string> Aplicacoes { get; } = new();
    public List<(string Nome, Action<Os.Domain.OrdensServico.OrdemDeServico> Acao)> Acoes { get; } = new();

    public SagaFixture(SagaTimeouts? timeouts = null)
    {
        var aplicador = new Mock<IAplicadorStatusOs>();
        aplicador.Setup(a => a.AplicarAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Action<Os.Domain.OrdensServico.OrdemDeServico>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, Action<Os.Domain.OrdensServico.OrdemDeServico>, CancellationToken>((_, nome, acao, _) => { lock (Aplicacoes) { Aplicacoes.Add(nome); Acoes.Add((nome, acao)); } })
            .Returns(Task.CompletedTask);

        Provider = new ServiceCollection()
            .AddSingleton(aplicador.Object)
            .AddSingleton(Microsoft.Extensions.Options.Options.Create(timeouts ?? new SagaTimeouts()))
            .AddLogging()
            .AddMassTransitTestHarness(x =>
            {
                x.AddDelayedMessageScheduler();
                x.AddSagaStateMachine<OrdemServicoStateMachine, OsSagaState>().InMemoryRepository();
                x.UsingInMemory((ctx, cfg) => { cfg.UseDelayedMessageScheduler(); cfg.ConfigureEndpoints(ctx); });
            })
            .BuildServiceProvider(true);
        Harness = Provider.GetRequiredService<ITestHarness>();
        Harness.Start().GetAwaiter().GetResult();
        Saga = Harness.GetSagaStateMachineHarness<OrdemServicoStateMachine, OsSagaState>();
    }

    public Task Pub<T>(T msg) where T : class => Harness.Bus.Publish(msg);

    public async Task<OsSagaState?> EsperarEstado(Guid id, Func<OrdemServicoStateMachine, MassTransit.State> estado)
    {
        var found = await Saga.Exists(id, estado, TimeSpan.FromSeconds(5));
        return found is null ? null : Saga.Sagas.Contains(found.Value);
    }

    public async ValueTask DisposeAsync() { await Harness.Stop(); await Provider.DisposeAsync(); }
}