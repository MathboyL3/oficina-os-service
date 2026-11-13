using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Os.Application.Catalogo;

namespace Os.Infrastructure.Catalogo;

/// <summary>
/// Cliente REST síncrono do catálogo do execucao-service. 404 vira <c>null</c>; qualquer outro
/// status de erro (401, 5xx) ou falha de transporte propaga como <see cref="HttpRequestException"/>.
/// </summary>
public sealed class CatalogoClient(HttpClient http) : ICatalogoClient
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private sealed record ServicoDto(Guid Id, string Nome, decimal ValorBase, int TempoEstimadoMinutos, bool Ativo);
    private sealed record PecaDto(Guid Id, string Nome, decimal Valor, bool Ativo);

    public async Task<ItemCatalogo?> ObterServicoAsync(Guid id, CancellationToken ct)
    {
        var dto = await ObterAsync<ServicoDto>($"api/servicos/{id}", ct);
        return dto is null ? null : new ItemCatalogo(dto.Id, dto.Nome, dto.ValorBase, dto.Ativo, dto.TempoEstimadoMinutos);
    }

    public async Task<ItemCatalogo?> ObterPecaAsync(Guid id, CancellationToken ct)
    {
        var dto = await ObterAsync<PecaDto>($"api/pecas/{id}", ct);
        return dto is null ? null : new ItemCatalogo(dto.Id, dto.Nome, dto.Valor, dto.Ativo, 0);
    }

    private async Task<T?> ObterAsync<T>(string url, CancellationToken ct) where T : class
    {
        using var resp = await http.GetAsync(url, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new HttpRequestException("catálogo recusou credenciais (401/403)", null, resp.StatusCode);
        resp.EnsureSuccessStatusCode();
        var corpo = await resp.Content.ReadAsStringAsync(ct);
        try
        {
            if (string.IsNullOrWhiteSpace(corpo)) throw Vazio(resp.StatusCode);
            return JsonSerializer.Deserialize<T>(corpo, JsonOpts) ?? throw Vazio(resp.StatusCode);
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException("resposta inválida do catálogo (JSON malformado)", ex, resp.StatusCode);
        }
    }

    private static HttpRequestException Vazio(HttpStatusCode status) =>
        new("resposta vazia do catálogo", null, status);
}

public static class CatalogoServiceCollectionExtensions
{
    public static IServiceCollection AddCatalogo(this IServiceCollection services, IConfiguration cfg)
    {
        var options = CatalogoOptions.FromConfiguration(cfg);
        services.AddHttpContextAccessor();
        services.AddTransient<RepassarJwtHandler>();
        services.AddHttpClient<ICatalogoClient, CatalogoClient>(c => c.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/"))
            .AddHttpMessageHandler<RepassarJwtHandler>()
            .AddStandardResilienceHandler();
        return services;
    }
}