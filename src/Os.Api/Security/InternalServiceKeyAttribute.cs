using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Os.Api.Security;

/// <summary>
/// Protege endpoints internos (service-to-service) pela chave <c>InternalApi:ServiceKey</c> enviada em <c>X-Service-Key</c>.
/// Filtro de autorização (e não middleware) para valer só nas actions marcadas e rodar antes do model binding.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class InternalServiceKeyAttribute : TypeFilterAttribute
{
    public const string HeaderName = "X-Service-Key";
    public const string ConfigKey = "InternalApi:ServiceKey";

    public InternalServiceKeyAttribute() : base(typeof(ServiceKeyFilter)) { }
}

public sealed class ServiceKeyFilter : IAsyncAuthorizationFilter
{
    private readonly IConfiguration _config;
    public ServiceKeyFilter(IConfiguration config) => _config = config;

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var expected = _config[InternalServiceKeyAttribute.ConfigKey];
        if (string.IsNullOrWhiteSpace(expected))
        {
            context.Result = Erro(StatusCodes.Status503ServiceUnavailable, "Endpoint interno indisponível: InternalApi:ServiceKey não configurada.");
            return Task.CompletedTask;
        }

        var provided = context.HttpContext.Request.Headers[InternalServiceKeyAttribute.HeaderName].ToString();
        if (!ServiceKeyComparer.IsValid(expected, provided))
            context.Result = Erro(StatusCodes.Status401Unauthorized, "Não autorizado.");

        return Task.CompletedTask;
    }

    private static ObjectResult Erro(int status, string message) =>
        new(new { status, message }) { StatusCode = status };
}