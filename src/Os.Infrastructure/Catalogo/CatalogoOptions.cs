using Microsoft.Extensions.Configuration;

namespace Os.Infrastructure.Catalogo;

/// <summary>Seção <c>Catalogo</c>: endereço do execucao-service.</summary>
public sealed record CatalogoOptions(string BaseUrl)
{
    public const string DefaultBaseUrl = "http://localhost:5003";

    public static CatalogoOptions FromConfiguration(IConfiguration cfg) =>
        new(cfg["Catalogo:BaseUrl"] ?? DefaultBaseUrl);
}