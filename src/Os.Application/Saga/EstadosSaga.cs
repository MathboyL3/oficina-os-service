namespace Os.Application.Saga;

/// <summary>
/// Nomes dos estados da Saga persistidos em <see cref="OsSagaState.CurrentState"/>.
/// Têm de bater com os <c>State</c> da state machine (Task 3.6).
/// </summary>
public static class EstadosSaga
{
    public const string Recebida = "Recebida",
        GerandoOrcamento = "GerandoOrcamento",
        AguardandoAprovacao = "AguardandoAprovacao",
        AguardandoPagamento = "AguardandoPagamento",
        ReservandoPecas = "ReservandoPecas",
        EmExecucao = "EmExecucao",
        Compensando = "Compensando",
        AguardandoAcaoManual = "AguardandoAcaoManual",
        Finalizada = "Finalizada",
        Cancelada = "Cancelada";

    /// <summary>Estados em que o cancelamento pelo cliente/atendente é aceito.</summary>
    public static readonly string[] Cancelaveis =
    {
        Recebida, GerandoOrcamento, AguardandoAprovacao, AguardandoPagamento, ReservandoPecas, EmExecucao
    };
}