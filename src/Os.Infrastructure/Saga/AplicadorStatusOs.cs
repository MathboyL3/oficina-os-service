using Os.Application.Common;
using Os.Application.Saga;
using Os.Domain.OrdensServico;

namespace Os.Infrastructure.Saga;

/// <summary>
/// Aplica a mudança de status no agregado rastreado pelo mesmo DbContext da Saga (mesma transação).
/// Conflito de concorrência (xmin) propaga para o retry do MassTransit.
/// </summary>
public sealed class AplicadorStatusOs : IAplicadorStatusOs
{
    private readonly IOrdemDeServicoRepository _repo;
    public AplicadorStatusOs(IOrdemDeServicoRepository repo) => _repo = repo;

    public async Task AplicarAsync(Guid osId, string nomeAcao, Action<OrdemDeServico> acao, CancellationToken ct)
    {
        var os = await _repo.ObterPorIdAsync(osId, ct) ?? throw new NotFoundException("Ordem de Serviço", osId);
        acao(os);
    }
}