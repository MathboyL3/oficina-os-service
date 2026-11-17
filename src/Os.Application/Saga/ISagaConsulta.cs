namespace Os.Application.Saga;

/// <summary>Leitura (somente consulta) do estado da Saga; quem escreve é a state machine.</summary>
public interface ISagaConsulta
{
    Task<OsSagaState?> ObterAsync(Guid osId, CancellationToken ct);
    Task<IReadOnlyList<OsSagaState>> ListarPorEstadoAsync(string estado, CancellationToken ct);
}