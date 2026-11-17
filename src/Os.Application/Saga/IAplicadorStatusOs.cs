using Os.Domain.OrdensServico;

namespace Os.Application.Saga;

/// <summary>
/// Aplica uma mudança de status no agregado da OS dentro da transação da Saga (sem SaveChanges:
/// quem persiste é o repositório da Saga, no mesmo DbContext). <paramref name="nomeAcao"/> serve a logs e testes.
/// </summary>
public interface IAplicadorStatusOs
{
    Task AplicarAsync(Guid osId, string nomeAcao, Action<OrdemDeServico> acao, CancellationToken ct);
}