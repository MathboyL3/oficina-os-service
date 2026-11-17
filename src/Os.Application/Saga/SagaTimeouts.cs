namespace Os.Application.Saga;

/// <summary>Prazos por etapa da Saga (seção <c>Saga:Timeouts</c>).</summary>
public sealed class SagaTimeouts
{
    public const string Secao = "Saga:Timeouts";

    public TimeSpan GerandoOrcamento { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan AguardandoAprovacao { get; set; } = TimeSpan.FromHours(72);
    public TimeSpan AguardandoPagamento { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan ReservandoPecas { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan EmExecucao { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan Compensando { get; set; } = TimeSpan.FromMinutes(15);
}