namespace Os.Application.Catalogo;

/// <summary>Item do catálogo (serviço ou peça) do execucao-service. Fonte única de nome e valor da OS.</summary>
public sealed record ItemCatalogo(Guid Id, string Nome, decimal Valor, bool Ativo, int TempoEstimadoMinutos);

public interface ICatalogoClient
{
    /// <summary>null = não existe (404).</summary>
    Task<ItemCatalogo?> ObterServicoAsync(Guid id, CancellationToken ct);
    Task<ItemCatalogo?> ObterPecaAsync(Guid id, CancellationToken ct);
}