using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace Os.Infrastructure.Messaging;

/// <summary>
/// Readiness do broker. A conexão é criada sob demanda (nunca no startup, para a API subir com o
/// RabbitMQ fora), reaproveitada enquanto aberta e descartada no encerramento do host (singleton).
/// Falha de conexão vira Unhealthy.
/// </summary>
public sealed class RabbitMqHealthCheck(RabbitMqOptions options) : IHealthCheck, IAsyncDisposable
{
    private static readonly TimeSpan TimeoutConexao = TimeSpan.FromSeconds(3);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _conexao;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_conexao is not { IsOpen: true })
            {
                if (_conexao is not null) await _conexao.DisposeAsync();
                _conexao = null;
                var factory = options.CreateConnectionFactory();
                factory.RequestedConnectionTimeout = TimeoutConexao;
                _conexao = await factory.CreateConnectionAsync(ct);
            }
            return HealthCheckResult.Healthy();
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ indisponível", e);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_conexao is not null) await _conexao.DisposeAsync();
        _lock.Dispose();
    }
}