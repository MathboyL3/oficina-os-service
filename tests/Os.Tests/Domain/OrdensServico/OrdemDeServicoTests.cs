using FluentAssertions;
using Os.Domain.Common;
using Os.Domain.OrdensServico;
using Os.Domain.ValueObjects;

namespace Os.Tests.Domain.OrdensServico;

public class OrdemDeServicoTests
{
    private readonly Guid _clienteId = Guid.NewGuid();
    private readonly Guid _veiculoId = Guid.NewGuid();

    private OrdemDeServico NovaOS() => OrdemDeServico.Abrir(_clienteId, _veiculoId);

    [Fact]
    public void Abrir_DeveIniciarComStatusRecebida()
    {
        var os = NovaOS();
        os.Status.Should().Be(StatusOS.Recebida);
        os.ValorTotal.Amount.Should().Be(0);
        os.Historico.Should().HaveCount(1);
    }

    [Fact]
    public void Abrir_SemCliente_DeveLancar()
    {
        Action act = () => OrdemDeServico.Abrir(Guid.Empty, _veiculoId);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void IncluirServico_DeveAumentarValorTotal()
    {
        var os = NovaOS();
        os.IncluirServico(Guid.NewGuid(), "Troca de óleo", Money.Brl(100), 1);
        os.IncluirServico(Guid.NewGuid(), "Alinhamento", Money.Brl(80), 2);
        os.ValorTotal.Amount.Should().Be(260);
    }

    [Fact]
    public void IncluirPeca_DeveAumentarValorTotal()
    {
        var os = NovaOS();
        os.IncluirServico(Guid.NewGuid(), "Diagnóstico", Money.Brl(50), 1);
        os.IncluirPeca(Guid.NewGuid(), "Filtro de óleo", Money.Brl(30), 2);
        os.ValorTotal.Amount.Should().Be(110);
    }

    [Fact]
    public void RemoverServico_DeveRecalcularTotal()
    {
        var os = NovaOS();
        os.IncluirServico(Guid.NewGuid(), "S1", Money.Brl(100), 1);
        var item = os.Servicos.First();
        os.RemoverServico(item.Id);
        os.Servicos.Should().BeEmpty();
        os.ValorTotal.Amount.Should().Be(0);
    }

    [Fact]
    public void RemoverServicoInexistente_DeveLancar()
    {
        var os = NovaOS();
        Action act = () => os.RemoverServico(Guid.NewGuid());
        act.Should().Throw<DomainException>().WithMessage("*não encontrado*");
    }

    [Fact]
    public void IniciarDiagnostico_DePassoInicial_DeveTransicionarParaEmDiagnostico()
    {
        var os = NovaOS();
        os.IniciarDiagnostico("Avaliando freios");
        os.Status.Should().Be(StatusOS.EmDiagnostico);
        os.ObservacoesDiagnostico.Should().Be("Avaliando freios");
    }

    [Fact]
    public void EnviarParaAprovacao_SemItens_DeveLancar()
    {
        var os = NovaOS();
        os.IniciarDiagnostico();
        Action act = () => os.EnviarParaAprovacao();
        act.Should().Throw<DomainException>().WithMessage("*sem itens*");
    }

    [Fact]
    public void FluxoCompleto_DeveTransicionarTodosOsStatusEmOrdem()
    {
        var os = NovaOS();
        os.IniciarDiagnostico("ok");
        os.IncluirServico(Guid.NewGuid(), "Troca de óleo", Money.Brl(100), 1);
        os.EnviarParaAprovacao();
        os.Status.Should().Be(StatusOS.AguardandoAprovacao);
        os.AprovarOrcamento();
        os.Status.Should().Be(StatusOS.AguardandoPagamento);
        os.ConfirmarPagamento();
        os.Status.Should().Be(StatusOS.EmExecucao);
        os.IniciadaExecucaoEm.Should().NotBeNull();
        os.Finalizar();
        os.Status.Should().Be(StatusOS.Finalizada);
        os.FinalizadaEm.Should().NotBeNull();
        os.TempoExecucao.Should().NotBeNull();
        os.Entregar();
        os.Status.Should().Be(StatusOS.Entregue);
        os.EntregueEm.Should().NotBeNull();
    }

    [Fact]
    public void TransicaoForaDeOrdem_DeveLancar()
    {
        var os = NovaOS();
        Action act = () => os.AprovarOrcamento();
        act.Should().Throw<DomainException>().WithMessage("*Transição inválida*");
    }

    [Fact]
    public void IncluirServico_AposEnviarParaAprovacao_DeveLancar()
    {
        var os = NovaOS();
        os.IniciarDiagnostico();
        os.IncluirServico(Guid.NewGuid(), "S1", Money.Brl(50), 1);
        os.EnviarParaAprovacao();
        Action act = () => os.IncluirServico(Guid.NewGuid(), "S2", Money.Brl(30), 1);
        act.Should().Throw<DomainException>().WithMessage("*AguardandoAprovacao*");
    }

    [Fact]
    public void Cancelar_OSEntregue_DeveLancar()
    {
        var os = NovaOS();
        os.IniciarDiagnostico();
        os.IncluirServico(Guid.NewGuid(), "S1", Money.Brl(50), 1);
        os.EnviarParaAprovacao();
        os.AprovarOrcamento();
        os.ConfirmarPagamento();
        os.Finalizar();
        os.Entregar();
        Action act = () => os.Cancelar("tarde");
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Cancelar_AntesDaEntrega_DevePermitir()
    {
        var os = NovaOS();
        os.IniciarDiagnostico();
        os.Cancelar("desistência do cliente");
        os.Status.Should().Be(StatusOS.Cancelada);
        os.Historico.Last().Para.Should().Be(StatusOS.Cancelada);
    }

    [Fact]
    public void Fluxo_fase4_aprovacao_pagamento_execucao()
    {
        var os = OrdemDeServico.Abrir(Guid.NewGuid(), Guid.NewGuid());
        os.IncluirServico(Guid.NewGuid(), "Revisão", Money.Brl(100m), 1);
        os.EnviarParaAprovacao(); // direto de Recebida
        os.Status.Should().Be(StatusOS.AguardandoAprovacao);
        os.AprovarOrcamento();
        os.Status.Should().Be(StatusOS.AguardandoPagamento);
        os.IniciadaExecucaoEm.Should().BeNull();
        FluentActions.Invoking(() => os.IncluirServico(Guid.NewGuid(), "x", Money.Brl(1m), 1)).Should().Throw<DomainException>();
        os.ConfirmarPagamento();
        os.Status.Should().Be(StatusOS.EmExecucao);
        os.IniciadaExecucaoEm.Should().NotBeNull();
    }

    [Fact]
    public void ConfirmarPagamento_fora_de_AguardandoPagamento_lanca()
    {
        var os = OrdemDeServico.Abrir(Guid.NewGuid(), Guid.NewGuid());
        FluentActions.Invoking(os.ConfirmarPagamento).Should().Throw<DomainException>();
    }

    [Fact]
    public void AguardandoPagamento_vale_31()
        => ((int)StatusOS.AguardandoPagamento).Should().Be(31);

    [Fact]
    public void RegistrarObservacao_nao_muda_status()
    {
        var os = OrdemDeServico.Abrir(Guid.NewGuid(), Guid.NewGuid());
        os.RegistrarObservacao("compensação pendente: estorno falhou");
        os.Status.Should().Be(StatusOS.Recebida);
        os.Historico.Last().Observacao.Should().Be("compensação pendente: estorno falhou");
    }

    [Fact]
    public void SubstituirItens_troca_tudo_e_recalcula()
    {
        var os = OrdemDeServico.Abrir(Guid.NewGuid(), Guid.NewGuid());
        os.IncluirServico(Guid.NewGuid(), "a", Money.Brl(10m), 1);
        os.SubstituirItens(new[] { (Guid.NewGuid(), "b", Money.Brl(20m), 2) }, new[] { (Guid.NewGuid(), "p", Money.Brl(5m), 1) });
        os.Servicos.Should().ContainSingle(s => s.Descricao == "b");
        os.ValorTotal.Amount.Should().Be(45m);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PodeCancelar_reflete_status(bool finalizar)
    {
        var os = OrdemDeServico.Abrir(Guid.NewGuid(), Guid.NewGuid());
        os.PodeCancelar.Should().BeTrue();
        if (finalizar) { os.Cancelar("x"); os.PodeCancelar.Should().BeFalse(); }
    }

    [Fact]
    public void SubstituirItens_invalido_mantem_itens_e_total()
    {
        var os = OrdemDeServico.Abrir(Guid.NewGuid(), Guid.NewGuid());
        os.IncluirServico(Guid.NewGuid(), "a", Money.Brl(10m), 1);
        FluentActions.Invoking(() => os.SubstituirItens(
                new[] { (Guid.NewGuid(), "b", Money.Brl(20m), 2) },
                new[] { (Guid.NewGuid(), "p", Money.Brl(5m), 0) }))
            .Should().Throw<DomainException>();
        os.Servicos.Should().ContainSingle(s => s.Descricao == "a");
        os.ValorTotal.Amount.Should().Be(10m);
    }

    [Fact]
    public void SubstituirItens_em_AguardandoPagamento_lanca()
    {
        var os = OrdemDeServico.Abrir(Guid.NewGuid(), Guid.NewGuid());
        os.IncluirServico(Guid.NewGuid(), "a", Money.Brl(10m), 1);
        os.EnviarParaAprovacao();
        os.AprovarOrcamento();
        FluentActions.Invoking(() => os.SubstituirItens(new[] { (Guid.NewGuid(), "b", Money.Brl(1m), 1) }, Array.Empty<(Guid, string, Money, int)>()))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void EnviarParaAprovacao_a_partir_de_EmDiagnostico_com_itens()
    {
        var os = OrdemDeServico.Abrir(Guid.NewGuid(), Guid.NewGuid());
        os.IniciarDiagnostico();
        os.IncluirServico(Guid.NewGuid(), "a", Money.Brl(10m), 1);
        os.EnviarParaAprovacao();
        os.Status.Should().Be(StatusOS.AguardandoAprovacao);
    }

    [Fact]
    public void EnviarParaAprovacao_a_partir_de_EmExecucao_lanca()
    {
        var os = OrdemDeServico.Abrir(Guid.NewGuid(), Guid.NewGuid());
        os.IncluirServico(Guid.NewGuid(), "a", Money.Brl(10m), 1);
        os.EnviarParaAprovacao();
        os.AprovarOrcamento();
        os.ConfirmarPagamento();
        FluentActions.Invoking(os.EnviarParaAprovacao).Should().Throw<DomainException>();
    }

    [Fact]
    public void Cancelar_em_Finalizada_lanca()
    {
        var os = EmStatus(StatusOS.Finalizada);
        FluentActions.Invoking(() => os.Cancelar("x")).Should().Throw<DomainException>();
        os.Status.Should().Be(StatusOS.Finalizada);
    }

    [Theory]
    [InlineData(StatusOS.Recebida)]
    [InlineData(StatusOS.EmDiagnostico)]
    [InlineData(StatusOS.AguardandoAprovacao)]
    [InlineData(StatusOS.AguardandoPagamento)]
    [InlineData(StatusOS.EmExecucao)]
    [InlineData(StatusOS.Finalizada)]
    [InlineData(StatusOS.Entregue)]
    [InlineData(StatusOS.Cancelada)]
    public void PodeCancelar_coerente_com_Cancelar(StatusOS status)
    {
        var os = EmStatus(status);
        if (os.PodeCancelar)
        {
            os.Cancelar("x");
            os.Status.Should().Be(StatusOS.Cancelada);
        }
        else
        {
            FluentActions.Invoking(() => os.Cancelar("x")).Should().Throw<DomainException>();
        }
    }

    private static OrdemDeServico EmStatus(StatusOS status)
    {
        var os = OrdemDeServico.Abrir(Guid.NewGuid(), Guid.NewGuid());
        if (status == StatusOS.Recebida) return os;
        if (status == StatusOS.EmDiagnostico) { os.IniciarDiagnostico(); return os; }
        os.IncluirServico(Guid.NewGuid(), "a", Money.Brl(10m), 1);
        os.EnviarParaAprovacao();
        if (status == StatusOS.AguardandoAprovacao) return os;
        os.AprovarOrcamento();
        if (status == StatusOS.AguardandoPagamento) return os;
        os.ConfirmarPagamento();
        if (status == StatusOS.EmExecucao) return os;
        if (status == StatusOS.Cancelada) { os.Cancelar("x"); return os; }
        os.Finalizar();
        if (status == StatusOS.Finalizada) return os;
        os.Entregar();
        return os;
    }
}
