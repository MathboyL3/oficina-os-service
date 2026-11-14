using Serilog.Core;
using Serilog.Events;

namespace Os.Api.Security;

/// <summary>
/// Rotas /internal/* carregam dado pessoal no path (CPF). Troca o último segmento por <c>{valor}</c> tanto no
/// request log quanto no escopo <c>RequestPath</c> que o ASP.NET Core anexa a todo log emitido durante a requisição.
/// </summary>
public sealed class MascaraPathEnricher : ILogEventEnricher
{
    public static string Mascarar(string path)
    {
        if (!path.StartsWith("/internal/", StringComparison.OrdinalIgnoreCase)) return path;
        var i = path.TrimEnd('/').LastIndexOf('/');
        return i <= 0 ? "/internal/{valor}" : path[..(i + 1)] + "{valor}";
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (logEvent.Properties.TryGetValue("RequestPath", out var v) && v is ScalarValue { Value: string path })
        {
            var mascarado = Mascarar(path);
            if (mascarado != path)
                logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("RequestPath", mascarado));
        }
    }
}