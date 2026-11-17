// Contratos de mensagens da Fase 4 (Saga de Ordem de Serviço).
// ATENÇÃO: arquivo idêntico nos 3 serviços — o MassTransit roteia pelo nome completo do tipo.
// Toda mensagem é correlacionada por OrdemServicoId (CorrelationId da Saga).
namespace Oficina.Contracts;

public interface IMensagemOS
{
    Guid OrdemServicoId { get; }
    DateTime Timestamp { get; }
}

public sealed record ItemOrcamento(string Tipo, Guid ItemId, string Descricao, decimal ValorUnitario, int Quantidade);
public sealed record ItemPecaQtd(Guid PecaId, int Quantidade);
public sealed record ItemServicoQtd(Guid ServicoId, int Quantidade);

// ---- os-service (API) → saga ----
public sealed record OrdemServicoAberta(Guid OrdemServicoId, DateTime Timestamp, Guid ClienteId, Guid VeiculoId) : IMensagemOS;
public sealed record OrcamentoSolicitado(Guid OrdemServicoId, DateTime Timestamp, Guid ClienteId, IReadOnlyList<ItemOrcamento> Itens, decimal ValorTotal) : IMensagemOS;
public sealed record CancelamentoSolicitado(Guid OrdemServicoId, DateTime Timestamp, string Motivo) : IMensagemOS;
public sealed record ReprocessarCompensacao(Guid OrdemServicoId, DateTime Timestamp) : IMensagemOS;

// ---- saga → billing ----
public sealed record GerarOrcamento(Guid OrdemServicoId, DateTime Timestamp, Guid ClienteId, IReadOnlyList<ItemOrcamento> Itens, decimal ValorTotal) : IMensagemOS;
public sealed record CriarCobranca(Guid OrdemServicoId, DateTime Timestamp, Guid OrcamentoId, decimal Valor) : IMensagemOS;
public sealed record CancelarOrcamento(Guid OrdemServicoId, DateTime Timestamp, string Motivo) : IMensagemOS;
public sealed record EstornarPagamento(Guid OrdemServicoId, DateTime Timestamp, Guid PagamentoId, string Motivo) : IMensagemOS;

// ---- billing → saga ----
public sealed record OrcamentoGerado(Guid OrdemServicoId, DateTime Timestamp, Guid OrcamentoId, decimal ValorTotal, DateTime ValidoAte) : IMensagemOS;
public sealed record GeracaoOrcamentoFalhou(Guid OrdemServicoId, DateTime Timestamp, string Motivo) : IMensagemOS;
public sealed record OrcamentoAprovado(Guid OrdemServicoId, DateTime Timestamp, Guid OrcamentoId) : IMensagemOS;
public sealed record OrcamentoRecusado(Guid OrdemServicoId, DateTime Timestamp, Guid OrcamentoId, string Motivo) : IMensagemOS;
public sealed record CobrancaCriada(Guid OrdemServicoId, DateTime Timestamp, Guid PagamentoId, string LinkPagamento) : IMensagemOS;
public sealed record PagamentoConfirmado(Guid OrdemServicoId, DateTime Timestamp, Guid PagamentoId) : IMensagemOS;
public sealed record PagamentoRecusado(Guid OrdemServicoId, DateTime Timestamp, Guid PagamentoId, string Motivo) : IMensagemOS;
public sealed record PagamentoEstornado(Guid OrdemServicoId, DateTime Timestamp, Guid PagamentoId) : IMensagemOS;
public sealed record EstornoFalhou(Guid OrdemServicoId, DateTime Timestamp, Guid PagamentoId, string Motivo) : IMensagemOS;
public sealed record OrcamentoCancelado(Guid OrdemServicoId, DateTime Timestamp, Guid? OrcamentoId) : IMensagemOS;

// ---- saga → execucao ----
public sealed record ReservarPecas(Guid OrdemServicoId, DateTime Timestamp, IReadOnlyList<ItemPecaQtd> Pecas) : IMensagemOS;
public sealed record EnfileirarExecucao(Guid OrdemServicoId, DateTime Timestamp, IReadOnlyList<ItemServicoQtd> Servicos, IReadOnlyList<ItemPecaQtd> Pecas) : IMensagemOS;
public sealed record LiberarPecas(Guid OrdemServicoId, DateTime Timestamp, string Motivo) : IMensagemOS;

// ---- execucao → saga ----
public sealed record PecasReservadas(Guid OrdemServicoId, DateTime Timestamp) : IMensagemOS;
public sealed record ReservaPecasFalhou(Guid OrdemServicoId, DateTime Timestamp, string Motivo) : IMensagemOS;
public sealed record ExecucaoIniciada(Guid OrdemServicoId, DateTime Timestamp, Guid ExecucaoId) : IMensagemOS;
public sealed record ExecucaoFinalizada(Guid OrdemServicoId, DateTime Timestamp, Guid ExecucaoId) : IMensagemOS;
public sealed record ExecucaoFalhou(Guid OrdemServicoId, DateTime Timestamp, Guid ExecucaoId, string Motivo) : IMensagemOS;
public sealed record PecasLiberadas(Guid OrdemServicoId, DateTime Timestamp) : IMensagemOS;

// ---- saga → (pub) ----
public sealed record OrdemServicoFinalizada(Guid OrdemServicoId, DateTime Timestamp) : IMensagemOS;
public sealed record OrdemServicoCancelada(Guid OrdemServicoId, DateTime Timestamp, string Motivo) : IMensagemOS;

// ---- interno da saga (Quartz) ----
public sealed record TimeoutEtapa(Guid OrdemServicoId, DateTime Timestamp, string Etapa) : IMensagemOS;
