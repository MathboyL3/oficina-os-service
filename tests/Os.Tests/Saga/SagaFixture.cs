using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Serilog;
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

    public SagaFixture(SagaTimeouts? timeouts = null, Serilog.Core.ILogEventSink? sink = null)
    {
        var aplicador = new Mock<IAplicadorStatusOs>();
        aplicador.Setup(a => a.AplicarAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Action<Os.Domain.OrdensServico.OrdemDeServico>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, Action<Os.Domain.OrdensServico.OrdemDeServico>, CancellationToken>((_, nome, acao, _) => { lock (Aplicacoes) { Aplicacoes.Add(nome); Acoes.Add((nome, acao)); } })
            .Returns(Task.CompletedTask);

        Provider = new ServiceCollection()
            .AddSingleton(aplicador.Object)
            .AddSingleton(Microsoft.Extensions.Options.Options.Create(timeouts ?? new SagaTimeouts()))
            .AddLogging(l =>
            {
                if (sink is not null)
                    l.AddSerilog(new Serilog.LoggerConfiguration().WriteTo.Sink(sink).CreateLogger(), dispose: true);
            })
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

    /// <summary>Faz polling (teto de 5s) até a mensagem ser publicada: o Any do harness desiste ao detectar ociosidade, antes de a saga terminar.</summary>
    public async Task<bool> Publicou<T>(Func<IPublishedMessage<T>, bool>? filtro = null) where T : class
    {
        var limite = DateTime.UtcNow.AddSeconds(5);
        do
        {
            if (await Harness.Published.Any<T>(x => filtro?.Invoke(x) ?? true)) return true;
            await Task.Delay(50);
        } while (DateTime.UtcNow < limite);
        return false;
    }

    public async ValueTask DisposeAsync() { await Harness.Stop(); await Provider.DisposeAsync(); }
}