using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace Os.Infrastructure.Messaging;

/// <summary>
/// Único lugar de leitura das chaves <c>RabbitMq:*</c>, usado pelo transporte do MassTransit
/// e pelo health check de readiness. A factory é montada campo a campo (e não por URI):
/// senha ou vhost com / # ? @ quebrariam o parsing de uma <see cref="Uri"/>.
/// </summary>
public sealed record RabbitMqOptions(string Host, int Port, string VirtualHost, string Username, string Password)
{
    public const int DefaultPort = 5672;

    public static RabbitMqOptions FromConfiguration(IConfiguration cfg) => new(
        cfg["RabbitMq:Host"] ?? "localhost",
        int.TryParse(cfg["RabbitMq:Port"], out var port) ? port : DefaultPort,
        cfg["RabbitMq:VirtualHost"] ?? "/",
        cfg["RabbitMq:Username"] ?? "guest",
        cfg["RabbitMq:Password"] ?? "guest");

    public ConnectionFactory CreateConnectionFactory() => new()
    {
        HostName = Host,
        Port = Port,
        VirtualHost = VirtualHost,
        UserName = Username,
        Password = Password
    };
}
