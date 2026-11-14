using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Os.Infrastructure.Messaging;

namespace Os.Tests.Infrastructure;

public class RabbitMqHealthCheckTests
{
    [Fact]
    public async Task Broker_fora_devolve_Unhealthy_sem_lancar()
    {
        // porta 1 em localhost: conexão recusada de imediato
        await using var check = new RabbitMqHealthCheck(new RabbitMqOptions("localhost", 1, "/", "guest", "guest"));

        var r = await check.CheckHealthAsync(new HealthCheckContext());

        r.Status.Should().Be(HealthStatus.Unhealthy);
        r.Exception.Should().NotBeNull();
    }
}