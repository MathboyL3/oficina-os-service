using Microsoft.AspNetCore.Http;

namespace Os.Infrastructure.Catalogo;

/// <summary>Repassa o <c>Authorization</c> da requisição atual para a chamada ao catálogo.</summary>
public sealed class RepassarJwtHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var auth = accessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(auth))
            request.Headers.TryAddWithoutValidation("Authorization", auth);
        return base.SendAsync(request, ct);
    }
}