using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oficina.Contracts;
using Os.Domain.OrdensServico;

namespace Os.Application.Saga;

/// <summary>
/// Orquestrador da Saga da Ordem de Serviço (abre → orçamento → aprovação → pagamento →
/// reserva de peças → execução), com compensação em cada etapa que pode falhar.
/// Estado persistido em os_saga_state; timeouts via scheduler (Quartz em produção).
/// </summary>
public sealed class OrdemServicoStateMachine : MassTransitStateMachine<OsSagaState>
{
    public State Recebida { get; private set; } = null!;
    public State GerandoOrcamento { get; private set; } = null!;
    public State AguardandoAprovacao { get; private set; } = null!;
    public State AguardandoPagamento { get; private set; } = null!;
    public State ReservandoPecas { get; private set; } = null!;
    public State EmExecucao { get; private set; } = null!;
    public State Compensando { get; private set; } = null!;
    public State AguardandoAcaoManual { get; private set; } = null!;
    public State Finalizada { get; private set; } = null!;
    public State Cancelada { get; private set; } = null!;

    public Event<OrdemServicoAberta> Aberta { get; private set; } = null!;
    public Event<OrcamentoSolicitado> OrcSolicitado { get; private set; } = null!;
    public Event<CancelamentoSolicitado> CancSolicitado { get; private set; } = null!;
    public Event<ReprocessarCompensacao> Reprocessar { get; private set; } = null!;
    public Event<OrcamentoGerado> OrcGerado { get; private set; } = null!;
    public Event<GeracaoOrcamentoFalhou> OrcFalhou { get; private set; } = null!;
    public Event<OrcamentoAprovado> OrcAprovado { get; private set; } = null!;
    public Event<OrcamentoRecusado> OrcRecusado { get; private set; } = null!;
    public Event<CobrancaCriada> CobCriada { get; private set; } = null!;
    public Event<PagamentoConfirmado> PagConfirmado { get; private set; } = null!;
    public Event<PagamentoRecusado> PagRecusado { get; private set; } = null!;
    public Event<PecasReservadas> Reservadas { get; private set; } = null!;
    public Event<ReservaPecasFalhou> ReservaFalhou { get; private set; } = null!;
    public Event<ExecucaoIniciada> ExecIniciada { get; private set; } = null!;
    public Event<ExecucaoFinalizada> ExecFinalizada { get; private set; } = null!;
    public Event<ExecucaoFalhou> ExecFalhou { get; private set; } = null!;
    public Event<PagamentoEstornado> Estornado { get; private set; } = null!;
    public Event<EstornoFalhou> EstornoFalhouEv { get; private set; } = null!;
    public Event<OrcamentoCancelado> OrcCancelado { get; private set; } = null!;
    public Event<PecasLiberadas> Liberadas { get; private set; } = null!;

    public Schedule<OsSagaState, TimeoutEtapa> Timeout { get; private set; } = null!;

    public OrdemServicoStateMachine(IOptions<SagaTimeouts> timeouts)
    {
        var to = timeouts.Value;
        InstanceState(x => x.CurrentState);

        Event(() => Aberta, e => { e.CorrelateById(c => c.Message.OrdemServicoId); e.InsertOnInitial = true; e.SetSagaFactory(c => Nova(c.Message.OrdemServicoId)); });
        Event(() => OrcSolicitado, e => { e.CorrelateById(c => c.Message.OrdemServicoId); e.InsertOnInitial = true; e.SetSagaFactory(c => Nova(c.Message.OrdemServicoId)); });
        Event(() => CancSolicitado, e => { e.CorrelateById(c => c.Message.OrdemServicoId); e.InsertOnInitial = true; e.SetSagaFactory(c => Nova(c.Message.OrdemServicoId)); });
        Event(() => Reprocessar, e => e.CorrelateById(c => c.Message.OrdemServicoId));
        Event(() => OrcGerado, e => e.CorrelateById(c => c.Message.OrdemServicoId));
        Event(() => OrcFalhou, e => e.CorrelateById(c => c.Message.OrdemServicoId));
        Event(() => OrcAprovado, e => e.CorrelateById(c => c.Message.OrdemServicoId));
        Event(() => OrcRecusado, e => e.CorrelateById(c => c.Message.OrdemServicoId));
        Event(() => CobCriada, e => e.CorrelateById(c => c.Message.OrdemServicoId));
        Event(() => PagConfirmado, e => e.CorrelateById(c => c.Message.OrdemServicoId));
        Event(() => PagRecusado, e => e.CorrelateById(c => c.Message.OrdemServicoId));
        Event(() => Reservadas, e => e.CorrelateById(c => c.Message.OrdemServicoId));
        Event(() => ReservaFalhou, e => e.CorrelateById(c => c.Message.OrdemServicoId));
        Event(() => ExecIniciada, e => e.CorrelateById(c => c.Message.OrdemServicoId));
        Event(() => ExecFinalizada, e => e.CorrelateById(c => c.Message.OrdemServicoId));
        Event(() => ExecFalhou, e => e.CorrelateById(c => c.Message.OrdemServicoId));
        Event(() => Estornado, e => e.CorrelateById(c => c.Message.OrdemServicoId));
        Event(() => EstornoFalhouEv, e => e.CorrelateById(c => c.Message.OrdemServicoId));
        Event(() => OrcCancelado, e => e.CorrelateById(c => c.Message.OrdemServicoId));
        Event(() => Liberadas, e => e.CorrelateById(c => c.Message.OrdemServicoId));

        Schedule(() => Timeout, x => x.TimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromMinutes(5);
            s.Received = r => r.CorrelateById(c => c.Message.OrdemServicoId);
        });

        // Eventos tardios/duplicados em estados que não os tratam são descartados (§4.5).
        OnUnhandledEvent(ctx => ctx.Ignore());

        Initially(
            When(Aberta).Then(c => c.Saga.ClienteId = c.Message.ClienteId).Then(c => Log(c, "Recebida")).TransitionTo(Recebida),
            When(OrcSolicitado).Then(GravarOrcamentoSolicitado)
                .ThenAsync(c => Aplicar(c, "EnviarParaAprovacao", os => { if (os.Status is StatusOS.Recebida or StatusOS.EmDiagnostico) os.EnviarParaAprovacao(); }))
                .ThenAsync(EnviarGerarOrcamento)
                .Schedule(Timeout, c => Tmo(c.Saga, "GerandoOrcamento"), _ => to.GerandoOrcamento)
                .Then(c => Log(c, "GerandoOrcamento")).TransitionTo(GerandoOrcamento),
            // Cancelamento antes de OrdemServicoAberta ser consumido: cria a instância já compensando.
            Compensar(When(CancSolicitado), c => c.Message.Motivo, to));

        During(Recebida,
            When(OrcSolicitado).Then(GravarOrcamentoSolicitado)
                .ThenAsync(c => Aplicar(c, "EnviarParaAprovacao", os => { if (os.Status is StatusOS.Recebida or StatusOS.EmDiagnostico) os.EnviarParaAprovacao(); }))
                .ThenAsync(EnviarGerarOrcamento)
                .Schedule(Timeout, c => Tmo(c.Saga, "GerandoOrcamento"), _ => to.GerandoOrcamento)
                .Then(c => Log(c, "GerandoOrcamento")).TransitionTo(GerandoOrcamento));

        During(GerandoOrcamento,
            When(OrcGerado).Unschedule(Timeout)
                .Then(c => c.Saga.OrcamentoId = c.Message.OrcamentoId)
                .Schedule(Timeout, c => Tmo(c.Saga, "AguardandoAprovacao"), _ => to.AguardandoAprovacao)
                .Then(c => Log(c, "AguardandoAprovacao")).TransitionTo(AguardandoAprovacao),
            Compensar(When(OrcFalhou), c => $"Falha ao gerar orçamento: {c.Message.Motivo}", to),
            Compensar(When(Timeout.Received, EtapaAtual("GerandoOrcamento")), _ => "Timeout na geração do orçamento.", to));

        During(AguardandoAprovacao,
            When(OrcAprovado).Unschedule(Timeout)
                .ThenAsync(c => Aplicar(c, "AprovarOrcamento", os => { if (os.Status == StatusOS.AguardandoAprovacao) os.AprovarOrcamento(); }))
                .Publish(c => new CriarCobranca(c.Saga.CorrelationId, DateTime.UtcNow, c.Message.OrcamentoId, c.Saga.ValorTotal))
                .Schedule(Timeout, c => Tmo(c.Saga, "AguardandoPagamento"), _ => to.AguardandoPagamento)
                .Then(c => Log(c, "AguardandoPagamento")).TransitionTo(AguardandoPagamento),
            Compensar(When(OrcRecusado), c => $"Orçamento recusado: {c.Message.Motivo}", to),
            Compensar(When(Timeout.Received, EtapaAtual("AguardandoAprovacao")), _ => "Orçamento expirou sem aprovação.", to));

        During(AguardandoPagamento,
            When(CobCriada).Then(c => { c.Saga.PagamentoId = c.Message.PagamentoId; c.Saga.LinkPagamento = c.Message.LinkPagamento; }),
            When(PagConfirmado).Unschedule(Timeout)
                .Then(c => c.Saga.PagamentoId = c.Message.PagamentoId)
                .Publish(c => new ReservarPecas(c.Saga.CorrelationId, DateTime.UtcNow, Pecas(c.Saga)))
                .Schedule(Timeout, c => Tmo(c.Saga, "ReservandoPecas"), _ => to.ReservandoPecas)
                .Then(c => Log(c, "ReservandoPecas")).TransitionTo(ReservandoPecas),
            Compensar(When(PagRecusado), c => $"Pagamento recusado: {c.Message.Motivo}", to),
            Compensar(When(Timeout.Received, EtapaAtual("AguardandoPagamento")), _ => "Pagamento não confirmado no prazo.", to));

        During(ReservandoPecas,
            When(Reservadas).Unschedule(Timeout)
                .Then(c => c.Saga.PecasReservadas = true)
                .ThenAsync(c => Aplicar(c, "ConfirmarPagamento", os => { if (os.Status == StatusOS.AguardandoPagamento) os.ConfirmarPagamento(); }))
                .Publish(c => new EnfileirarExecucao(c.Saga.CorrelationId, DateTime.UtcNow, Servicos(c.Saga), Pecas(c.Saga)))
                .Schedule(Timeout, c => Tmo(c.Saga, "EmExecucao"), _ => to.EmExecucao)
                .Then(c => Log(c, "EmExecucao")).TransitionTo(EmExecucao),
            Compensar(When(ReservaFalhou), c => $"Falha na reserva de peças: {c.Message.Motivo}", to),
            Compensar(When(Timeout.Received, EtapaAtual("ReservandoPecas")), _ => "Timeout na reserva de peças.", to));

        During(EmExecucao,
            When(ExecIniciada).ThenAsync(c => Aplicar(c, "Observacao", os => os.RegistrarObservacao("Diagnóstico/reparo iniciado pelo mecânico."))),
            When(ExecFinalizada).Unschedule(Timeout)
                .ThenAsync(c => Aplicar(c, "Finalizar", os => { if (os.Status == StatusOS.EmExecucao) os.Finalizar(); }))
                .Publish(c => new OrdemServicoFinalizada(c.Saga.CorrelationId, DateTime.UtcNow))
                .Then(c => Log(c, "Finalizada")).TransitionTo(Finalizada).Finalize(),
            Compensar(When(ExecFalhou), c => $"Falha na execução: {c.Message.Motivo}", to),
            // EmExecucao: timeout só alerta (não compensa) — §4.2.
            When(Timeout.Received, EtapaAtual("EmExecucao")).Then(c => c.GetServiceOrCreateInstance<ILogger<OrdemServicoStateMachine>>()
                .LogError("saga.alerta {alerta} execução parada há mais de {Prazo} os={OsId}", "saga_em_execucao_atrasada", to.EmExecucao, c.Saga.CorrelationId)));

        During(new[] { Recebida, GerandoOrcamento, AguardandoAprovacao, AguardandoPagamento, ReservandoPecas, EmExecucao },
            Compensar(When(CancSolicitado), c => c.Message.Motivo, to));

        During(Compensando,
            When(CobCriada).ThenAsync(c => CorridaPagamento(c, c.Message.PagamentoId)),
            When(PagConfirmado).ThenAsync(c => CorridaPagamento(c, c.Message.PagamentoId)),
            ConcluirSeOk(When(OrcCancelado).Then(c => c.Saga.OrcamentoCanceladoOk = true)),
            ConcluirSeOk(When(Liberadas).Then(c => c.Saga.PecasLiberadasOk = true)),
            ConcluirSeOk(When(Estornado).Then(c => c.Saga.EstornoOk = true)),
            When(EstornoFalhouEv).Unschedule(Timeout)
                .ThenAsync(c => IrParaAcaoManual(c, $"estorno falhou: {c.Message.Motivo}")).TransitionTo(AguardandoAcaoManual),
            When(Timeout.Received, EtapaAtual("Compensando")).Unschedule(Timeout)
                .ThenAsync(c => IrParaAcaoManual(c, "compensação não confirmada no prazo")).TransitionTo(AguardandoAcaoManual));

        During(AguardandoAcaoManual,
            ConcluirSeOk(When(OrcCancelado).Then(c => c.Saga.OrcamentoCanceladoOk = true)),
            ConcluirSeOk(When(Liberadas).Then(c => c.Saga.PecasLiberadasOk = true)),
            ConcluirSeOk(When(Estornado).Then(c => c.Saga.EstornoOk = true)),
            When(Reprocessar).ThenAsync(ReenviarPendentes)
                .Schedule(Timeout, c => Tmo(c.Saga, "Compensando"), _ => to.Compensando)
                .Then(c => Log(c, "Compensando")).TransitionTo(Compensando));

        SetCompletedWhenFinalized();
    }

    // ---------------------------------------------------------------- blocos reutilizáveis (binder)

    /// <summary>Entrada em Compensando: grava motivo/flags, publica compensações e agenda o timeout da etapa.</summary>
    private EventActivityBinder<OsSagaState, T> Compensar<T>(EventActivityBinder<OsSagaState, T> b,
        Func<BehaviorContext<OsSagaState, T>, string> motivo, SagaTimeouts to) where T : class
        => b.Unschedule(Timeout)
            .ThenAsync(c => PrepararCompensacao(c, motivo(c)))
            .Schedule(Timeout, c => Tmo(c.Saga, "Compensando"), _ => to.Compensando)
            .Then(c => Log(c, "Compensando"))
            .TransitionTo(Compensando);

    /// <summary>Após cada confirmação: se as três compensações estão ok, cancela a OS e encerra a Saga.</summary>
    private EventActivityBinder<OsSagaState, T> ConcluirSeOk<T>(EventActivityBinder<OsSagaState, T> b) where T : class
        => b.If(c => c.Saga.OrcamentoCanceladoOk && c.Saga.PecasLiberadasOk && c.Saga.EstornoOk,
            ok => ok.Unschedule(Timeout)
                .ThenAsync(Concluir)
                .Then(c => Log(c, "Cancelada"))
                .TransitionTo(Cancelada)
                .Finalize());

    // ---------------------------------------------------------------- helpers

    private static OsSagaState Nova(Guid id) => new() { CorrelationId = id, CriadaEm = DateTime.UtcNow, AtualizadaEm = DateTime.UtcNow };

    /// <summary>Timeout só vale se for da etapa do estado atual (valores gravados por <see cref="Tmo"/>).</summary>
    private static StateMachineCondition<OsSagaState, TimeoutEtapa> EtapaAtual(string etapa) => c => c.Message.Etapa == etapa;

    private static TimeoutEtapa Tmo(OsSagaState s, string etapa) => new(s.CorrelationId, DateTime.UtcNow, etapa);

    private static void GravarOrcamentoSolicitado(BehaviorContext<OsSagaState, OrcamentoSolicitado> c)
    {
        c.Saga.ClienteId = c.Message.ClienteId;
        c.Saga.ValorTotal = c.Message.ValorTotal;
        c.Saga.ItensJson = JsonSerializer.Serialize(c.Message.Itens);
    }

    private static Task EnviarGerarOrcamento(BehaviorContext<OsSagaState, OrcamentoSolicitado> c)
        => c.Publish(new GerarOrcamento(c.Saga.CorrelationId, DateTime.UtcNow, c.Message.ClienteId, c.Message.Itens, c.Message.ValorTotal));

    private static List<ItemOrcamento> Itens(OsSagaState s) => JsonSerializer.Deserialize<List<ItemOrcamento>>(s.ItensJson) ?? new();
    private static IReadOnlyList<ItemPecaQtd> Pecas(OsSagaState s) => Itens(s).Where(i => i.Tipo == "Peca").Select(i => new ItemPecaQtd(i.ItemId, i.Quantidade)).ToList();
    private static IReadOnlyList<ItemServicoQtd> Servicos(OsSagaState s) => Itens(s).Where(i => i.Tipo == "Servico").Select(i => new ItemServicoQtd(i.ItemId, i.Quantidade)).ToList();

    private static Task Aplicar<T>(BehaviorContext<OsSagaState, T> c, string nome, Action<OrdemDeServico> acao) where T : class
    {
        c.Saga.AtualizadaEm = DateTime.UtcNow;
        return c.GetServiceOrCreateInstance<IAplicadorStatusOs>().AplicarAsync(c.Saga.CorrelationId, nome, acao, c.CancellationToken);
    }

    private static void Log<T>(BehaviorContext<OsSagaState, T> c, string estado) where T : class
        => c.GetServiceOrCreateInstance<ILogger<OrdemServicoStateMachine>>()
            .LogInformation("saga.estado {SagaEstado} os={OsId}", estado, c.Saga.CorrelationId);

    /// <summary>
    /// Só dados + publicações (o agendamento do timeout fica no binder, em Compensar).
    /// CurrentState ainda é o estado de origem aqui (o TransitionTo vem depois).
    /// LiberarPecas sai sempre que a origem é ReservandoPecas/EmExecucao: a reserva pode ter sido
    /// commitada no execucao-service com o PecasReservadas ainda em voo (o consumidor tolera "sem reserva").
    /// </summary>
    private static async Task PrepararCompensacao<T>(BehaviorContext<OsSagaState, T> c, string motivo) where T : class
    {
        var s = c.Saga;
        var liberar = s.PecasReservadas || s.CurrentState is EstadosSaga.ReservandoPecas or EstadosSaga.EmExecucao;
        s.MotivoCancelamento = motivo;
        s.OrcamentoCanceladoOk = false;
        s.PecasLiberadasOk = !liberar;
        s.EstornoOk = s.PagamentoId is null;
        s.AtualizadaEm = DateTime.UtcNow;

        await c.Publish(new CancelarOrcamento(s.CorrelationId, DateTime.UtcNow, motivo));
        if (liberar) await c.Publish(new LiberarPecas(s.CorrelationId, DateTime.UtcNow, motivo));
        if (s.PagamentoId is { } pag) await c.Publish(new EstornarPagamento(s.CorrelationId, DateTime.UtcNow, pag, motivo));
    }

    /// <summary>Pagamento que chega depois de iniciada a compensação sem pagamento conhecido → estorna.</summary>
    private static async Task CorridaPagamento<T>(BehaviorContext<OsSagaState, T> c, Guid pagamentoId) where T : class
    {
        if (c.Saga.PagamentoId == pagamentoId && c.Saga.EstornoOk) return; // estorno já confirmado (duplicado)
        if (c.Saga.PagamentoId is not null && !c.Saga.EstornoOk) return; // estorno já pedido
        c.Saga.PagamentoId = pagamentoId;
        c.Saga.EstornoOk = false;
        await c.Publish(new EstornarPagamento(c.Saga.CorrelationId, DateTime.UtcNow, pagamentoId, c.Saga.MotivoCancelamento ?? "OS cancelada"));
    }

    private static async Task Concluir<T>(BehaviorContext<OsSagaState, T> c) where T : class
    {
        var motivo = c.Saga.MotivoCancelamento ?? "OS cancelada.";
        await Aplicar(c, "Cancelar", os => { if (os.PodeCancelar) os.Cancelar(motivo); });
        await c.Publish(new OrdemServicoCancelada(c.Saga.CorrelationId, DateTime.UtcNow, motivo));
    }

    private static async Task IrParaAcaoManual<T>(BehaviorContext<OsSagaState, T> c, string motivo) where T : class
    {
        await Aplicar(c, "Observacao", os => os.RegistrarObservacao($"compensação pendente: {motivo}"));
        c.GetServiceOrCreateInstance<ILogger<OrdemServicoStateMachine>>()
            .LogError("saga.alerta {alerta} saga.estado {SagaEstado} os={OsId} motivo={Motivo}", "saga_acao_manual", "AguardandoAcaoManual", c.Saga.CorrelationId, motivo);
    }

    private static async Task ReenviarPendentes(BehaviorContext<OsSagaState, ReprocessarCompensacao> c)
    {
        var s = c.Saga;
        var motivo = s.MotivoCancelamento ?? "reprocessamento";
        if (!s.OrcamentoCanceladoOk) await c.Publish(new CancelarOrcamento(s.CorrelationId, DateTime.UtcNow, motivo));
        if (!s.PecasLiberadasOk) await c.Publish(new LiberarPecas(s.CorrelationId, DateTime.UtcNow, motivo));
        if (!s.EstornoOk && s.PagamentoId is { } pag) await c.Publish(new EstornarPagamento(s.CorrelationId, DateTime.UtcNow, pag, motivo));
    }
}