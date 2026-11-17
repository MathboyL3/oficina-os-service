namespace Os.Application.DTOs;

public record AbrirOSRequest(Guid ClienteId, Guid VeiculoId);

public record ItemQtdRequest(Guid Id, int Quantidade);

public record DefinirItensRequest(IReadOnlyList<ItemQtdRequest> Servicos, IReadOnlyList<ItemQtdRequest> Pecas);

public record IniciarDiagnosticoRequest(string? Observacoes);

public record CancelarOSRequest(string Motivo);

public record ItemServicoResponse(Guid Id, Guid ServicoId, string Descricao, decimal ValorUnitario, int Quantidade, decimal Subtotal);

public record ItemPecaResponse(Guid Id, Guid PecaId, string Descricao, decimal ValorUnitario, int Quantidade, decimal Subtotal);

public record HistoricoStatusResponse(string De, string Para, DateTime OcorreuEm, string? Observacao);

public record SagaResumo(string Estado, Guid? OrcamentoId, Guid? PagamentoId, string? LinkPagamento, string? MotivoCancelamento);

public record OSResponse(
    Guid Id,
    Guid ClienteId,
    Guid VeiculoId,
    string Status,
    decimal ValorTotal,
    DateTime CriadaEm,
    DateTime? IniciadaExecucaoEm,
    DateTime? FinalizadaEm,
    DateTime? EntregueEm,
    string? ObservacoesDiagnostico,
    IReadOnlyList<ItemServicoResponse> Servicos,
    IReadOnlyList<ItemPecaResponse> Pecas,
    IReadOnlyList<HistoricoStatusResponse> Historico,
    SagaResumo? Saga = null);

public record OSResumoResponse(
    Guid Id,
    Guid ClienteId,
    Guid VeiculoId,
    string Status,
    decimal ValorTotal,
    DateTime CriadaEm);

public record PendenciaResponse(Guid OrdemServicoId, SagaResumo Saga);

public record TempoMedioExecucaoResponse(int Quantidade, double TempoMedioMinutos, double TempoMedioHoras);

public record StatusPublicoResponse(Guid Id, string Status, DateTime CriadaEm, DateTime? EstimativaConclusao);