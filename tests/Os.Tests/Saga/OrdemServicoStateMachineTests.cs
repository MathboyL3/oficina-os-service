using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Oficina.Contracts;
using Os.Application.Saga;

namespace Os.Tests.Saga;

public class OrdemServicoStateMachineTests
{
    private static readonly DateTime T = DateTime.UtcNow;
    private static readonly ItemOrcamento[] Itens =
    {
        new("Servico", Guid.NewGuid(), "Revisão", 100m, 1),
        new("Peca", Guid.NewGuid(), "Filtro", 20m, 2)
    };

    private static async Task<Guid> AteAguardandoPagamento(SagaFixture f)
    {
        var os = Guid.NewGuid();
        await f.Pub(new OrdemServicoAberta(os, T, Guid.NewGuid(), Guid.NewGuid()));
        await f.Pub(new OrcamentoSolicitado(os, T, Guid.NewGuid(), Itens, 140m));
        (await f.EsperarEstado(os, m => m.GerandoOrcamento)).Should().NotBeNull();
        (await f.Harness.Published.Any<GerarOrcamento>(x => x.Context.Message.OrdemServicoId == os)).Should().BeTrue();
        var orc = Guid.NewGuid();
        await f.Pub(new OrcamentoGerado(os, T, orc, 140m, T.AddDays(3)));
        (await f.EsperarEstado(os, m => m.AguardandoAprovacao)).Should().NotBeNull();
        await f.Pub(new OrcamentoAprovado(os, T, orc));
        (await f.EsperarEstado(os, m => m.AguardandoPagamento)).Should().NotBeNull();
        (await f.Harness.Published.Any<CriarCobranca>(x => x.Context.Message.Valor == 140m)).Should().BeTrue();
        return os;
    }

    [Fact]
    public async Task Caminho_feliz_ate_Finalizada()
    {
        await using var f = new SagaFixture();
        var os = await AteAguardandoPagamento(f);
        var pag = Guid.NewGuid();
        await f.Pub(new CobrancaCriada(os, T, pag, "http://link"));
        await f.Pub(new PagamentoConfirmado(os, T, pag));
        (await f.EsperarEstado(os, m => m.ReservandoPecas)).Should().NotBeNull();
        (await f.Harness.Published.Any<ReservarPecas>(x => x.Context.Message.Pecas.Single().Quantidade == 2)).Should().BeTrue();
        await f.Pub(new PecasReservadas(os, T));
        (await f.EsperarEstado(os, m => m.EmExecucao)).Should().NotBeNull();
        (await f.Harness.Published.Any<EnfileirarExecucao>()).Should().BeTrue();
        await f.Pub(new ExecucaoIniciada(os, T, Guid.NewGuid()));
        await f.Pub(new ExecucaoFinalizada(os, T, Guid.NewGuid()));
        (await f.Harness.Published.Any<OrdemServicoFinalizada>(x => x.Context.Message.OrdemServicoId == os)).Should().BeTrue();
        (await f.Saga.NotExists(os, TimeSpan.FromSeconds(5))).Should().BeNull(); // finalizada e removida
        f.Aplicacoes.Should().ContainInOrder("EnviarParaAprovacao", "AprovarOrcamento", "ConfirmarPagamento", "Finalizar");
    }

    [Fact]
    public async Task Orcamento_recusado_compensa_e_cancela()
    {
        await using var f = new SagaFixture();
        var os = Guid.NewGuid();
        await f.Pub(new OrdemServicoAberta(os, T, Guid.NewGuid(), Guid.NewGuid()));
        await f.Pub(new OrcamentoSolicitado(os, T, Guid.NewGuid(), Itens, 140m));
        var orc = Guid.NewGuid();
        await f.Pub(new OrcamentoGerado(os, T, orc, 140m, T.AddDays(3)));
        (await f.EsperarEstado(os, m => m.AguardandoAprovacao)).Should().NotBeNull();
        await f.Pub(new OrcamentoRecusado(os, T, orc, "caro"));
        (await f.EsperarEstado(os, m => m.Compensando)).Should().NotBeNull();
        (await f.Harness.Published.Any<CancelarOrcamento>()).Should().BeTrue();
        (await f.Harness.Published.Any<EstornarPagamento>()).Should().BeFalse();
        (await f.Harness.Published.Any<LiberarPecas>()).Should().BeFalse();
        await f.Pub(new OrcamentoCancelado(os, T, orc));
        (await f.Publicou<OrdemServicoCancelada>(x => x.Context.Message.Motivo.Contains("caro") && x.Context.Message.Motivo.Contains("Orçamento recusado"))).Should().BeTrue();
        (await f.Saga.NotExists(os, TimeSpan.FromSeconds(5))).Should().BeNull(); // Cancelada e removida
        f.Aplicacoes.Should().Contain("Cancelar");
    }

    [Fact]
    public async Task Falta_de_pecas_estorna_e_cancela()
    {
        await using var f = new SagaFixture();
        var os = await AteAguardandoPagamento(f);
        var pag = Guid.NewGuid();
        await f.Pub(new PagamentoConfirmado(os, T, pag));
        (await f.EsperarEstado(os, m => m.ReservandoPecas)).Should().NotBeNull();
        await f.Pub(new ReservaPecasFalhou(os, T, "Estoque insuficiente"));
        (await f.EsperarEstado(os, m => m.Compensando)).Should().NotBeNull();
        (await f.Harness.Published.Any<EstornarPagamento>(x => x.Context.Message.PagamentoId == pag)).Should().BeTrue();
        // origem ReservandoPecas: LiberarPecas sai sempre (reserva pode ter sido commitada com o evento em voo)
        (await f.Harness.Published.Any<LiberarPecas>()).Should().BeTrue();
        await f.Pub(new PagamentoEstornado(os, T, pag));
        await f.Pub(new PecasLiberadas(os, T));
        (await f.EsperarEstado(os, m => m.Compensando)).Should().NotBeNull(); // ainda falta OrcamentoCancelado
        await f.Pub(new OrcamentoCancelado(os, T, null));
        (await f.Publicou<OrdemServicoCancelada>()).Should().BeTrue();
        (await f.Saga.NotExists(os, TimeSpan.FromSeconds(5))).Should().BeNull(); // Cancelada e removida
    }

    [Fact]
    public async Task Falha_na_execucao_libera_pecas_e_estorna()
    {
        await using var f = new SagaFixture();
        var os = await AteAguardandoPagamento(f);
        var pag = Guid.NewGuid();
        await f.Pub(new PagamentoConfirmado(os, T, pag));
        await f.Pub(new PecasReservadas(os, T));
        (await f.EsperarEstado(os, m => m.EmExecucao)).Should().NotBeNull();
        await f.Pub(new ExecucaoFalhou(os, T, Guid.NewGuid(), "motor"));
        (await f.EsperarEstado(os, m => m.Compensando)).Should().NotBeNull();
        (await f.Harness.Published.Any<LiberarPecas>()).Should().BeTrue();
        (await f.Harness.Published.Any<EstornarPagamento>()).Should().BeTrue();
        await f.Pub(new PecasLiberadas(os, T));
        await f.Pub(new PagamentoEstornado(os, T, pag));
        await f.Pub(new OrcamentoCancelado(os, T, null));
        (await f.Publicou<OrdemServicoCancelada>()).Should().BeTrue();
        (await f.Saga.NotExists(os, TimeSpan.FromSeconds(5))).Should().BeNull(); // Cancelada e removida
    }

    [Fact]
    public async Task Estorno_falhou_vai_para_AguardandoAcaoManual_e_reprocessa()
    {
        await using var f = new SagaFixture();
        var os = await AteAguardandoPagamento(f);
        var pag = Guid.NewGuid();
        await f.Pub(new PagamentoConfirmado(os, T, pag));
        await f.Pub(new ReservaPecasFalhou(os, T, "sem estoque"));
        (await f.EsperarEstado(os, m => m.Compensando)).Should().NotBeNull();
        await f.Pub(new EstornoFalhou(os, T, pag, "MP fora"));
        var s = await f.EsperarEstado(os, m => m.AguardandoAcaoManual);
        s.Should().NotBeNull();
        f.Aplicacoes.Should().Contain("Observacao").And.NotContain("Cancelar");
        await f.Pub(new OrcamentoCancelado(os, T, null)); // confirmação atrasada só marca a flag
        await f.Pub(new ReprocessarCompensacao(os, T));
        (await f.EsperarEstado(os, m => m.Compensando)).Should().NotBeNull();
        (await f.Harness.Published.SelectAsync<EstornarPagamento>().Count()).Should().Be(2);
        (await f.Harness.Published.SelectAsync<CancelarOrcamento>().Count()).Should().Be(1); // não reenviou o já confirmado
        (await f.Harness.Published.SelectAsync<LiberarPecas>().Count()).Should().Be(2);       // ainda pendente → reenviado
        await f.Pub(new PecasLiberadas(os, T));
        await f.Pub(new PagamentoEstornado(os, T, pag));
        (await f.Publicou<OrdemServicoCancelada>()).Should().BeTrue();
        (await f.Saga.NotExists(os, TimeSpan.FromSeconds(5))).Should().BeNull(); // Cancelada e removida
    }

    [Fact]
    public async Task Timeout_de_aprovacao_compensa()
    {
        await using var f = new SagaFixture(new SagaTimeouts { AguardandoAprovacao = TimeSpan.FromMilliseconds(300) });
        var os = Guid.NewGuid();
        await f.Pub(new OrcamentoSolicitado(os, T, Guid.NewGuid(), Itens, 140m)); // antes de OrdemServicoAberta: Review Focus 1
        await f.Pub(new OrcamentoGerado(os, T, Guid.NewGuid(), 140m, T));
        (await f.EsperarEstado(os, m => m.Compensando)).Should().NotBeNull();
        (await f.Harness.Published.Any<CancelarOrcamento>()).Should().BeTrue();
    }

    [Fact]
    public async Task Timeout_em_execucao_nao_compensa()
    {
        await using var f = new SagaFixture(new SagaTimeouts { EmExecucao = TimeSpan.FromMilliseconds(300) });
        var os = await AteAguardandoPagamento(f);
        await f.Pub(new PagamentoConfirmado(os, T, Guid.NewGuid()));
        await f.Pub(new PecasReservadas(os, T));
        (await f.EsperarEstado(os, m => m.EmExecucao)).Should().NotBeNull();
        (await f.Saga.Consumed.Any<TimeoutEtapa>(x => x.Context.Message.Etapa == "EmExecucao")).Should().BeTrue(); // timeout chegou e foi tratado
        (await f.EsperarEstado(os, m => m.EmExecucao)).Should().NotBeNull();
        (await f.Harness.Published.Any<CancelarOrcamento>()).Should().BeFalse();
    }

    [Fact]
    public async Task Cancelamento_em_execucao_libera_pecas()
    {
        await using var f = new SagaFixture();
        var os = await AteAguardandoPagamento(f);
        await f.Pub(new PagamentoConfirmado(os, T, Guid.NewGuid()));
        await f.Pub(new PecasReservadas(os, T));
        (await f.EsperarEstado(os, m => m.EmExecucao)).Should().NotBeNull();
        await f.Pub(new CancelamentoSolicitado(os, T, "cliente desistiu"));
        (await f.EsperarEstado(os, m => m.Compensando)).Should().NotBeNull();
        (await f.Harness.Published.Any<LiberarPecas>()).Should().BeTrue();
    }

    [Fact]
    public async Task Duplicado_nao_reaplica()
    {
        await using var f = new SagaFixture();
        var os = await AteAguardandoPagamento(f);
        var pag = Guid.NewGuid();
        await f.Pub(new PagamentoConfirmado(os, T, pag));
        await f.Pub(new PagamentoConfirmado(os, T, pag));
        (await f.EsperarEstado(os, m => m.ReservandoPecas)).Should().NotBeNull();
        await Task.Delay(300);
        (await f.Harness.Published.SelectAsync<ReservarPecas>().Count()).Should().Be(1);
        f.Aplicacoes.Count(a => a == "AprovarOrcamento").Should().Be(1);
    }

    [Fact]
    public async Task Pagamento_aprovado_apos_compensar_sem_pagamento_dispara_estorno()
    {
        await using var f = new SagaFixture();
        var os = await AteAguardandoPagamento(f);
        await f.Pub(new PagamentoRecusado(os, T, Guid.NewGuid(), "rejected"));
        (await f.EsperarEstado(os, m => m.Compensando)).Should().NotBeNull();
        var pag = Guid.NewGuid();
        await f.Pub(new PagamentoConfirmado(os, T, pag)); // corrida
        (await f.Harness.Published.Any<EstornarPagamento>(x => x.Context.Message.PagamentoId == pag)).Should().BeTrue();
    }

    [Fact]
    public async Task Cancelamento_antes_de_aberta_cria_instancia_em_Compensando()
    {
        await using var f = new SagaFixture();
        var os = Guid.NewGuid();
        await f.Pub(new CancelamentoSolicitado(os, T, "desistiu"));
        (await f.EsperarEstado(os, m => m.Compensando)).Should().NotBeNull();
        (await f.Harness.Published.Any<CancelarOrcamento>()).Should().BeTrue();
        await f.Pub(new OrdemServicoAberta(os, T, Guid.NewGuid(), Guid.NewGuid())); // tardia: ignorada
        await f.Pub(new OrcamentoCancelado(os, T, null));
        (await f.Publicou<OrdemServicoCancelada>(x => x.Context.Message.OrdemServicoId == os)).Should().BeTrue();
        (await f.Saga.NotExists(os, TimeSpan.FromSeconds(5))).Should().BeNull();
    }

    [Fact]
    public async Task OrcamentoSolicitado_antes_de_aberta_cria_instancia()
    {
        await using var f = new SagaFixture();
        var os = Guid.NewGuid();
        await f.Pub(new OrcamentoSolicitado(os, T, Guid.NewGuid(), Itens, 140m));
        (await f.EsperarEstado(os, m => m.GerandoOrcamento)).Should().NotBeNull();
        await f.Pub(new OrdemServicoAberta(os, T, Guid.NewGuid(), Guid.NewGuid())); // tardia: ignorada
        (await f.EsperarEstado(os, m => m.GerandoOrcamento)).Should().NotBeNull();
    }

    [Fact]
    public async Task Timeout_de_geracao_do_orcamento_compensa()
    {
        await using var f = new SagaFixture(new SagaTimeouts { GerandoOrcamento = TimeSpan.FromMilliseconds(300) });
        var os = Guid.NewGuid();
        await f.Pub(new OrcamentoSolicitado(os, T, Guid.NewGuid(), Itens, 140m));
        (await f.EsperarEstado(os, m => m.Compensando)).Should().NotBeNull();
        (await f.Harness.Published.Any<CancelarOrcamento>()).Should().BeTrue();
    }

    [Fact]
    public async Task Geracao_falhou_compensa()
    {
        await using var f = new SagaFixture();
        var os = Guid.NewGuid();
        await f.Pub(new OrcamentoSolicitado(os, T, Guid.NewGuid(), Itens, 140m));
        (await f.EsperarEstado(os, m => m.GerandoOrcamento)).Should().NotBeNull();
        await f.Pub(new GeracaoOrcamentoFalhou(os, T, "sem preço"));
        (await f.EsperarEstado(os, m => m.Compensando)).Should().NotBeNull();
    }

    [Fact]
    public async Task Timeout_de_compensacao_vai_para_AguardandoAcaoManual()
    {
        await using var f = new SagaFixture(new SagaTimeouts { Compensando = TimeSpan.FromMilliseconds(300) });
        var os = Guid.NewGuid();
        await f.Pub(new CancelamentoSolicitado(os, T, "x"));
        (await f.EsperarEstado(os, m => m.AguardandoAcaoManual)).Should().NotBeNull();
        f.Aplicacoes.Should().Contain("Observacao").And.NotContain("Cancelar");
    }

    [Fact]
    public async Task Cancelamento_em_estados_cancelaveis_compensa()
    {
        await using var f = new SagaFixture();
        var os = await AteAguardandoPagamento(f);
        await f.Pub(new CancelamentoSolicitado(os, T, "cliente desistiu"));
        (await f.EsperarEstado(os, m => m.Compensando)).Should().NotBeNull();
        (await f.Harness.Published.Any<LiberarPecas>()).Should().BeFalse();

        await using var g = new SagaFixture();
        var os2 = Guid.NewGuid();
        await g.Pub(new OrdemServicoAberta(os2, T, Guid.NewGuid(), Guid.NewGuid()));
        (await g.EsperarEstado(os2, m => m.Recebida)).Should().NotBeNull();
        await g.Pub(new CancelamentoSolicitado(os2, T, "x"));
        (await g.EsperarEstado(os2, m => m.Compensando)).Should().NotBeNull();
    }

    [Fact]
    public async Task Acoes_aplicadas_sao_idempotentes_no_agregado()
    {
        await using var f = new SagaFixture();
        var os = Guid.NewGuid();
        await f.Pub(new OrcamentoSolicitado(os, T, Guid.NewGuid(), Itens, 140m));
        await f.Pub(new CancelamentoSolicitado(os, T, "x"));
        await f.Pub(new OrcamentoCancelado(os, T, null));
        (await f.Publicou<OrdemServicoCancelada>(x => x.Context.Message.OrdemServicoId == os)).Should().BeTrue();
        (await f.Saga.NotExists(os, TimeSpan.FromSeconds(5))).Should().BeNull();
        var cancelar = f.Acoes.Single(a => a.Nome == "Cancelar").Acao;
        var agregado = Os.Domain.OrdensServico.OrdemDeServico.Abrir(Guid.NewGuid(), Guid.NewGuid());
        agregado.Cancelar("antes");
        var act = () => cancelar(agregado); // já Cancelada: não lança
        act.Should().NotThrow();
        var enviar = f.Acoes.First(a => a.Nome == "EnviarParaAprovacao").Acao;
        var act2 = () => enviar(agregado); // status incompatível: ignora em vez de lançar
        act2.Should().NotThrow();
    }

    [Fact]
    public async Task Timeout_de_etapa_anterior_e_ignorado()
    {
        await using var f = new SagaFixture();
        var os = await AteAguardandoPagamento(f);
        await f.Pub(new TimeoutEtapa(os, T, "GerandoOrcamento")); // atrasado: a saga já está em AguardandoPagamento
        (await f.Saga.Consumed.Any<TimeoutEtapa>(x => x.Context.Message.OrdemServicoId == os)).Should().BeTrue();
        (await f.EsperarEstado(os, m => m.AguardandoPagamento)).Should().NotBeNull();
        (await f.Harness.Published.Any<CancelarOrcamento>()).Should().BeFalse();
    }

    [Fact]
    public async Task Pagamento_confirmado_duplicado_apos_estorno_ok_nao_reenvia_estorno()
    {
        await using var f = new SagaFixture();
        var os = await AteAguardandoPagamento(f);
        var pag = Guid.NewGuid();
        await f.Pub(new CobrancaCriada(os, T, pag, "http://link"));
        await f.Pub(new CancelamentoSolicitado(os, T, "x"));
        (await f.EsperarEstado(os, m => m.Compensando)).Should().NotBeNull();
        (await f.Harness.Published.Any<EstornarPagamento>(x => x.Context.Message.PagamentoId == pag)).Should().BeTrue();
        await f.Pub(new PagamentoEstornado(os, T, pag));
        (await f.Saga.Consumed.Any<PagamentoEstornado>()).Should().BeTrue();
        await f.Pub(new PagamentoConfirmado(os, T, pag)); // duplicado/tardio com o estorno já confirmado
        (await f.Saga.Consumed.Any<PagamentoConfirmado>()).Should().BeTrue();
        (await f.Harness.Published.SelectAsync<EstornarPagamento>().Count()).Should().Be(1);
    }
}