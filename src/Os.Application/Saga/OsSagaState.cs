using MassTransit;

namespace Os.Application.Saga;

public class OsSagaState : SagaStateMachineInstance, ISagaVersion
{
    public Guid CorrelationId { get; set; }          // = OrdemServicoId
    public string CurrentState { get; set; } = null!;
    public Guid ClienteId { get; set; }
    public decimal ValorTotal { get; set; }
    public string ItensJson { get; set; } = "[]";     // List<ItemOrcamento> serializado (jsonb)
    public Guid? OrcamentoId { get; set; }
    public Guid? PagamentoId { get; set; }
    public string? LinkPagamento { get; set; }
    public bool PecasReservadas { get; set; }
    public bool OrcamentoCanceladoOk { get; set; }
    public bool PecasLiberadasOk { get; set; }
    public bool EstornoOk { get; set; }
    public string? MotivoCancelamento { get; set; }
    public Guid? TimeoutTokenId { get; set; }
    public int Version { get; set; }
    public DateTime CriadaEm { get; set; }
    public DateTime AtualizadaEm { get; set; }
}