using Microsoft.EntityFrameworkCore;
using Os.Application.Saga;
using Os.Infrastructure.Persistence;

namespace Os.Infrastructure.Saga;

/// <summary>Leitura do estado da Saga (sem rastreamento: a state machine é a única que escreve).</summary>
public sealed class SagaConsulta : ISagaConsulta
{
    private readonly OsDbContext _ctx;
    public SagaConsulta(OsDbContext ctx) => _ctx = ctx;

    public Task<OsSagaState?> ObterAsync(Guid osId, CancellationToken ct) =>
        _ctx.Sagas.AsNoTracking().FirstOrDefaultAsync(s => s.CorrelationId == osId, ct);

    public async Task<IReadOnlyList<OsSagaState>> ListarPorEstadoAsync(string estado, CancellationToken ct) =>
        await _ctx.Sagas.AsNoTracking().Where(s => s.CurrentState == estado).OrderBy(s => s.AtualizadaEm).ToListAsync(ct);
}