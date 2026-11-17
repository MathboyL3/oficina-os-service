using FluentAssertions;
using Oficina.Contracts;
using Os.Bdd.Support;
using Reqnroll;

namespace Os.Bdd.Steps;

[Binding]
public sealed class SagaSteps
{
    private readonly ScenarioContext _ctx;
    private readonly Guid _os = Guid.NewGuid();
    private readonly Guid _orcamento = Guid.NewGuid();
    private readonly Guid _pagamento = Guid.NewGuid();
    private IReadOnlyList<ItemOrcamento> _itens = Array.Empty<ItemOrcamento>();
    private decimal _total;

    public SagaSteps(ScenarioContext ctx) => _ctx = ctx;

    private SagaFixture _f => _ctx.Get<SagaFixture>();

    [Given("uma OS aberta com 1 serviço de R$ 100,00 e 2 peças de R$ 20,00")]
    public async Task OsAberta()
    {
        _itens = new[]
        {
            new ItemOrcamento("Servico", Guid.NewGuid(), "Revisão", 100m, 1),
            new ItemOrcamento("Peca", Guid.NewGuid(), "Filtro", 20m, 2)
        };
        _total = 140m;
        await _f.Pub(new OrdemServicoAberta(_os, DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid()));
    }

    [When("o orçamento é solicitado")]
    public async Task OrcamentoSolicitadoStep()
    {
        await _f.Pub(new OrcamentoSolicitado(_os, DateTime.UtcNow, Guid.NewGuid(), _itens, _total));
        (await _f.EsperarEstado(_os, m => m.GerandoOrcamento)).Should().NotBeNull();
        (await _f.Publicou<GerarOrcamento>(x => x.Context.Message.OrdemServicoId == _os)).Should().BeTrue();
    }

    [When("o billing gera o orçamento")]
    public async Task BillingGera()
    {
        await _f.Pub(new OrcamentoGerado(_os, DateTime.UtcNow, _orcamento, _total, DateTime.UtcNow.AddDays(3)));
        (await _f.EsperarEstado(_os, m => m.AguardandoAprovacao)).Should().NotBeNull();
    }

    [When("o cliente aprova o orçamento")]
    public async Task ClienteAprova()
    {
        await _f.Pub(new OrcamentoAprovado(_os, DateTime.UtcNow, _orcamento));
        (await _f.EsperarEstado(_os, m => m.AguardandoPagamento)).Should().NotBeNull();
        (await _f.Publicou<CriarCobranca>(x => x.Context.Message.OrdemServicoId == _os && x.Context.Message.Valor == _total)).Should().BeTrue();
    }

    [When("o cliente recusa o orçamento")]
    public async Task ClienteRecusa()
    {
        await _f.Pub(new OrcamentoRecusado(_os, DateTime.UtcNow, _orcamento, "caro"));
        (await _f.EsperarEstado(_os, m => m.Compensando)).Should().NotBeNull();
    }

    [When("o pagamento é confirmado")]
    public async Task PagamentoConfirmadoStep()
    {
        await _f.Pub(new CobrancaCriada(_os, DateTime.UtcNow, _pagamento, "http://link"));
        await _f.Pub(new PagamentoConfirmado(_os, DateTime.UtcNow, _pagamento));
        (await _f.EsperarEstado(_os, m => m.ReservandoPecas)).Should().NotBeNull();
        (await _f.Publicou<ReservarPecas>(x => x.Context.Message.OrdemServicoId == _os)).Should().BeTrue();
    }

    [When("o pagamento é recusado")]
    public async Task PagamentoRecusadoStep()
    {
        await _f.Pub(new PagamentoRecusado(_os, DateTime.UtcNow, _pagamento, "Saldo insuficiente"));
        (await _f.EsperarEstado(_os, m => m.Compensando)).Should().NotBeNull();
    }

    [When("as peças são reservadas")]
    public async Task PecasReservadasStep()
    {
        await _f.Pub(new PecasReservadas(_os, DateTime.UtcNow));
        (await _f.EsperarEstado(_os, m => m.EmExecucao)).Should().NotBeNull();
        (await _f.Publicou<EnfileirarExecucao>(x => x.Context.Message.OrdemServicoId == _os)).Should().BeTrue();
    }

    [When("a reserva de peças falha por falta de estoque")]
    public async Task ReservaFalha()
    {
        await _f.Pub(new ReservaPecasFalhou(_os, DateTime.UtcNow, "Estoque insuficiente"));
        (await _f.EsperarEstado(_os, m => m.Compensando)).Should().NotBeNull();
    }

    [When("a execução é finalizada")]
    public async Task ExecucaoFinalizadaStep()
    {
        var execucao = Guid.NewGuid();
        await _f.Pub(new ExecucaoIniciada(_os, DateTime.UtcNow, execucao));
        await _f.Pub(new ExecucaoFinalizada(_os, DateTime.UtcNow, execucao));
    }

    [When("a execução falha")]
    public async Task ExecucaoFalhaStep()
    {
        await _f.Pub(new ExecucaoFalhou(_os, DateTime.UtcNow, Guid.NewGuid(), "Defeito no motor"));
        (await _f.EsperarEstado(_os, m => m.Compensando)).Should().NotBeNull();
    }

    [Then("a OS deve ser finalizada")]
    public async Task OsFinalizada()
    {
        (await _f.Publicou<OrdemServicoFinalizada>(x => x.Context.Message.OrdemServicoId == _os)).Should().BeTrue();
        (await _f.Saga.NotExists(_os, TimeSpan.FromSeconds(5))).Should().BeNull(); // finalizada e removida
        _f.Aplicacoes.Should().Contain("Finalizar");
    }

    [Then("nenhuma compensação deve ser disparada")]
    public async Task SemCompensacao()
    {
        (await _f.Harness.Published.Any<CancelarOrcamento>()).Should().BeFalse();
        (await _f.Harness.Published.Any<EstornarPagamento>()).Should().BeFalse();
        (await _f.Harness.Published.Any<LiberarPecas>()).Should().BeFalse();
        (await _f.Harness.Published.Any<OrdemServicoCancelada>()).Should().BeFalse();
    }

    [Then("a Saga deve pedir o cancelamento do orçamento")]
    public async Task PedeCancelamentoOrcamento()
        => (await _f.Publicou<CancelarOrcamento>(x => x.Context.Message.OrdemServicoId == _os)).Should().BeTrue();

    [Then("a Saga deve pedir o estorno do pagamento")]
    public async Task PedeEstorno()
        => (await _f.Publicou<EstornarPagamento>(x => x.Context.Message.PagamentoId == _pagamento)).Should().BeTrue();

    [Then("a Saga deve pedir a liberação das peças")]
    public async Task PedeLiberacao()
        => (await _f.Publicou<LiberarPecas>(x => x.Context.Message.OrdemServicoId == _os)).Should().BeTrue();

    [Then("quando o billing confirmar o cancelamento a OS deve ser cancelada")]
    public async Task ConfirmaCancelamentoOrcamento()
    {
        // sem pagamento confirmado nem peças reservadas: só o orçamento precisa ser cancelado
        await _f.Pub(new OrcamentoCancelado(_os, DateTime.UtcNow, _orcamento));
        await AssertCancelada();
    }

    [Then("quando o estorno, a liberação das peças e o cancelamento forem confirmados a OS deve ser cancelada")]
    public async Task ConfirmaEstornoLiberacaoECancelamento()
    {
        await _f.Pub(new PagamentoEstornado(_os, DateTime.UtcNow, _pagamento));
        await _f.Pub(new PecasLiberadas(_os, DateTime.UtcNow));
        await _f.Pub(new OrcamentoCancelado(_os, DateTime.UtcNow, _orcamento));
        await AssertCancelada();
    }

    [Then("quando todas as compensações forem confirmadas a OS deve ser cancelada")]
    public Task ConfirmaTodas() => ConfirmaEstornoLiberacaoECancelamento();

    private async Task AssertCancelada()
    {
        (await _f.Publicou<OrdemServicoCancelada>(x => x.Context.Message.OrdemServicoId == _os)).Should().BeTrue();
        (await _f.Saga.NotExists(_os, TimeSpan.FromSeconds(5))).Should().BeNull(); // Cancelada e removida
        _f.Aplicacoes.Should().Contain("Cancelar");
    }
}