using FluentAssertions;
using MassTransit;
using Moq;
using Os.Application.Catalogo;
using Os.Application.Common;
using Os.Application.DTOs;
using Os.Application.Saga;
using Os.Application.UseCases.OrdensServico;
using Os.Domain.Clientes;
using Os.Domain.Common;
using Os.Domain.OrdensServico;
using Os.Domain.ValueObjects;
using Os.Domain.Veiculos;
using Oficina.Contracts;

namespace Os.Tests.Application;

public class OrdemServicoUseCasesTests
{
    private readonly Mock<IOrdemDeServicoRepository> _repo = new();
    private readonly Mock<IClienteRepository> _clientes = new();
    private readonly Mock<IVeiculoRepository> _veiculos = new();
    private readonly Mock<ICatalogoClient> _catalogo = new();
    private readonly Mock<ISagaConsulta> _saga = new();
    private readonly Mock<IPublishEndpoint> _bus = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private OrdemServicoUseCases Uc() =>
        new(_repo.Object, _clientes.Object, _veiculos.Object, _catalogo.Object, _saga.Object, _bus.Object, _uow.Object);

    private OrdemDeServico NovaOs()
    {
        var os = OrdemDeServico.Abrir(Guid.NewGuid(), Guid.NewGuid());
        _repo.Setup(r => r.ObterPorIdAsync(os.Id, It.IsAny<CancellationToken>())).ReturnsAsync(os);
        return os;
    }

    private static ItemCatalogo Item(Guid id, decimal valor = 80m, bool ativo = true, string nome = "Item", int min = 30) =>
        new(id, nome, valor, ativo, min);

    private void SagaEm(Guid osId, string estado) =>
        _saga.Setup(s => s.ObterAsync(osId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OsSagaState { CorrelationId = osId, CurrentState = estado });

    [Fact]
    public async Task Abrir_publica_OrdemServicoAberta()
    {
        var cliente = Cliente.Cadastrar("Joao", Documento.Parse("11144477735"), "j@x.com", "1");
        var veiculo = Veiculo.Cadastrar(cliente.Id, Placa.Create("ABC1D23"), "VW", "Gol", 2020);
        _clientes.Setup(c => c.ObterPorIdAsync(cliente.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cliente);
        _veiculos.Setup(v => v.ObterPorIdAsync(veiculo.Id, It.IsAny<CancellationToken>())).ReturnsAsync(veiculo);

        var resp = await Uc().AbrirAsync(new AbrirOSRequest(cliente.Id, veiculo.Id));

        _bus.Verify(b => b.Publish(
            It.Is<OrdemServicoAberta>(m => m.OrdemServicoId == resp.Id && m.ClienteId == cliente.Id && m.VeiculoId == veiculo.Id),
            It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DefinirItens_usa_preco_do_catalogo()
    {
        var os = NovaOs();
        var servicoId = Guid.NewGuid();
        _catalogo.Setup(c => c.ObterServicoAsync(servicoId, It.IsAny<CancellationToken>())).ReturnsAsync(Item(servicoId, 80m, nome: "Troca de oleo"));

        var resp = await Uc().DefinirItensAsync(os.Id,
            new DefinirItensRequest(new[] { new ItemQtdRequest(servicoId, 3) }, Array.Empty<ItemQtdRequest>()));

        resp.ValorTotal.Should().Be(240m);
        resp.Servicos.Single().Descricao.Should().Be("Troca de oleo");
        resp.Servicos.Single().ValorUnitario.Should().Be(80m);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DefinirItens_servico_inexistente_404_e_nao_altera()
    {
        var os = NovaOs();
        var servicoId = Guid.NewGuid();
        _catalogo.Setup(c => c.ObterServicoAsync(servicoId, It.IsAny<CancellationToken>())).ReturnsAsync((ItemCatalogo?)null);

        await FluentActions.Awaiting(() => Uc().DefinirItensAsync(os.Id,
                new DefinirItensRequest(new[] { new ItemQtdRequest(servicoId, 1) }, Array.Empty<ItemQtdRequest>())))
            .Should().ThrowAsync<NotFoundException>();

        os.Servicos.Should().BeEmpty();
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DefinirItens_peca_inativa_409()
    {
        var os = NovaOs();
        var pecaId = Guid.NewGuid();
        _catalogo.Setup(c => c.ObterPecaAsync(pecaId, It.IsAny<CancellationToken>())).ReturnsAsync(Item(pecaId, ativo: false));

        await FluentActions.Awaiting(() => Uc().DefinirItensAsync(os.Id,
                new DefinirItensRequest(Array.Empty<ItemQtdRequest>(), new[] { new ItemQtdRequest(pecaId, 1) })))
            .Should().ThrowAsync<ConflictException>();
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task DefinirItens_quantidade_zero_400(int qtd)
    {
        var os = NovaOs();

        var ex = await FluentActions.Awaiting(() => Uc().DefinirItensAsync(os.Id,
                new DefinirItensRequest(new[] { new ItemQtdRequest(Guid.NewGuid(), qtd) }, Array.Empty<ItemQtdRequest>())))
            .Should().ThrowAsync<AppException>();
        ex.Which.Should().NotBeOfType<ConflictException>().And.NotBeOfType<NotFoundException>();
        _catalogo.VerifyNoOtherCalls();
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DefinirItens_catalogo_com_nome_vazio_e_resposta_invalida(string nome)
    {
        var os = NovaOs();
        var servicoId = Guid.NewGuid();
        _catalogo.Setup(c => c.ObterServicoAsync(servicoId, It.IsAny<CancellationToken>())).ReturnsAsync(Item(servicoId, nome: nome));

        await FluentActions.Awaiting(() => Uc().DefinirItensAsync(os.Id,
                new DefinirItensRequest(new[] { new ItemQtdRequest(servicoId, 1) }, Array.Empty<ItemQtdRequest>())))
            .Should().ThrowAsync<HttpRequestException>().WithMessage("*Nome*");
        os.Servicos.Should().BeEmpty();
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DefinirItens_peca_usa_catalogo_e_os_inexistente_404()
    {
        var os = NovaOs();
        var pecaId = Guid.NewGuid();
        _catalogo.Setup(c => c.ObterPecaAsync(pecaId, It.IsAny<CancellationToken>())).ReturnsAsync(Item(pecaId, 10m, nome: "Filtro", min: 0));

        var resp = await Uc().DefinirItensAsync(os.Id,
            new DefinirItensRequest(Array.Empty<ItemQtdRequest>(), new[] { new ItemQtdRequest(pecaId, 2) }));
        resp.Pecas.Single().Subtotal.Should().Be(20m);

        await FluentActions.Awaiting(() => Uc().DefinirItensAsync(Guid.NewGuid(),
                new DefinirItensRequest(Array.Empty<ItemQtdRequest>(), Array.Empty<ItemQtdRequest>())))
            .Should().ThrowAsync<NotFoundException>();
    }

    private OrdemDeServico OsComItens()
    {
        var os = NovaOs();
        os.IncluirServico(Guid.NewGuid(), "Troca de oleo", Money.Brl(80m), 2);
        os.IncluirPeca(Guid.NewGuid(), "Filtro", Money.Brl(25m), 1);
        return os;
    }

    [Fact]
    public async Task EnviarOrcamento_publica_OrcamentoSolicitado_com_itens_e_total()
    {
        var os = OsComItens();

        await Uc().EnviarOrcamentoAsync(os.Id);

        _bus.Verify(b => b.Publish(
            It.Is<OrcamentoSolicitado>(m =>
                m.OrdemServicoId == os.Id && m.ClienteId == os.ClienteId && m.ValorTotal == 185m &&
                m.Itens.Count == 2 &&
                m.Itens.Any(i => i.Tipo == "Servico" && i.Descricao == "Troca de oleo" && i.ValorUnitario == 80m && i.Quantidade == 2) &&
                m.Itens.Any(i => i.Tipo == "Peca" && i.Descricao == "Filtro" && i.ValorUnitario == 25m && i.Quantidade == 1)),
            It.IsAny<CancellationToken>()), Times.Once);
        os.Status.Should().Be(StatusOS.Recebida); // quem aplica EnviarParaAprovacao é a Saga
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnviarOrcamento_sem_itens_422()
    {
        var os = NovaOs();

        await FluentActions.Awaiting(() => Uc().EnviarOrcamentoAsync(os.Id)).Should().ThrowAsync<DomainException>();
        _bus.Verify(b => b.Publish(It.IsAny<OrcamentoSolicitado>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnviarOrcamento_fora_de_Recebida_ou_EmDiagnostico_422()
    {
        var os = OsComItens();
        os.EnviarParaAprovacao();

        await FluentActions.Awaiting(() => Uc().EnviarOrcamentoAsync(os.Id)).Should().ThrowAsync<DomainException>();
        _bus.Verify(b => b.Publish(It.IsAny<OrcamentoSolicitado>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IniciarDiagnostico_muda_status_e_commita()
    {
        var os = NovaOs();

        var resp = await Uc().IniciarDiagnosticoAsync(os.Id, new IniciarDiagnosticoRequest("barulho"));

        resp.Status.Should().Be("EmDiagnostico");
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("Recebida")]
    [InlineData("GerandoOrcamento")]
    [InlineData("AguardandoAprovacao")]
    [InlineData("AguardandoPagamento")]
    [InlineData("ReservandoPecas")]
    [InlineData("EmExecucao")]
    public async Task Cancelar_em_estado_permitido_publica(string estado)
    {
        var os = NovaOs();
        SagaEm(os.Id, estado);

        await Uc().CancelarAsync(os.Id, new CancelarOSRequest("desistiu"));

        _bus.Verify(b => b.Publish(
            It.Is<CancelamentoSolicitado>(m => m.OrdemServicoId == os.Id && m.Motivo == "desistiu"),
            It.IsAny<CancellationToken>()), Times.Once);
        os.Status.Should().Be(StatusOS.Recebida); // quem muda o status é a Saga
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("Finalizada")]
    [InlineData("Cancelada")]
    [InlineData("Compensando")]
    [InlineData("AguardandoAcaoManual")]
    public async Task Cancelar_em_estado_terminal_409_sem_publicar(string estado)
    {
        var os = NovaOs();
        SagaEm(os.Id, estado);

        await FluentActions.Awaiting(() => Uc().CancelarAsync(os.Id, new CancelarOSRequest("x")))
            .Should().ThrowAsync<ConflictException>();
        _bus.Verify(b => b.Publish(It.IsAny<CancelamentoSolicitado>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancelar_sem_saga_e_os_cancelavel_publica()
    {
        var os = NovaOs();
        _saga.Setup(s => s.ObterAsync(os.Id, It.IsAny<CancellationToken>())).ReturnsAsync((OsSagaState?)null);

        await Uc().CancelarAsync(os.Id, new CancelarOSRequest("x"));

        _bus.Verify(b => b.Publish(It.IsAny<CancelamentoSolicitado>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancelar_sem_saga_e_os_nao_cancelavel_409()
    {
        var os = NovaOs();
        os.IncluirServico(Guid.NewGuid(), "S", Money.Brl(10m), 1);
        os.EnviarParaAprovacao();
        os.AprovarOrcamento();
        os.ConfirmarPagamento();
        os.Finalizar();
        _saga.Setup(s => s.ObterAsync(os.Id, It.IsAny<CancellationToken>())).ReturnsAsync((OsSagaState?)null);

        await FluentActions.Awaiting(() => Uc().CancelarAsync(os.Id, new CancelarOSRequest("x")))
            .Should().ThrowAsync<ConflictException>();
        _bus.Verify(b => b.Publish(It.IsAny<CancelamentoSolicitado>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancelar_os_ja_cancelada_com_saga_orfa_em_Recebida_409_sem_publicar()
    {
        var os = NovaOs();
        os.Cancelar("antes"); // instância de saga órfã: a OS já foi cancelada mas a saga seguiu em Recebida
        SagaEm(os.Id, "Recebida");

        await FluentActions.Awaiting(() => Uc().CancelarAsync(os.Id, new CancelarOSRequest("x")))
            .Should().ThrowAsync<ConflictException>();
        _bus.Verify(b => b.Publish(It.IsAny<CancelamentoSolicitado>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reprocessar_so_em_AguardandoAcaoManual()
    {
        var os = NovaOs();
        SagaEm(os.Id, "Compensando");
        await FluentActions.Awaiting(() => Uc().ReprocessarCompensacaoAsync(os.Id)).Should().ThrowAsync<ConflictException>();
        _bus.Verify(b => b.Publish(It.IsAny<ReprocessarCompensacao>(), It.IsAny<CancellationToken>()), Times.Never);

        SagaEm(os.Id, "AguardandoAcaoManual");
        await Uc().ReprocessarCompensacaoAsync(os.Id);

        _bus.Verify(b => b.Publish(It.Is<ReprocessarCompensacao>(m => m.OrdemServicoId == os.Id), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reprocessar_sem_saga_409()
    {
        var os = NovaOs();
        _saga.Setup(s => s.ObterAsync(os.Id, It.IsAny<CancellationToken>())).ReturnsAsync((OsSagaState?)null);

        await FluentActions.Awaiting(() => Uc().ReprocessarCompensacaoAsync(os.Id)).Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task ListarPendencias_devolve_sagas_em_AguardandoAcaoManual()
    {
        var id = Guid.NewGuid();
        _saga.Setup(s => s.ListarPorEstadoAsync(EstadosSaga.AguardandoAcaoManual, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new OsSagaState { CorrelationId = id, CurrentState = "AguardandoAcaoManual", MotivoCancelamento = "estorno" } });

        var lista = await Uc().ListarPendenciasAsync();

        lista.Should().ContainSingle().Which.Should().Match<PendenciaResponse>(p => p.OrdemServicoId == id && p.Saga.MotivoCancelamento == "estorno");
    }

    [Fact]
    public async Task Obter_inclui_SagaResumo()
    {
        var os = NovaOs();
        _saga.Setup(s => s.ObterAsync(os.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OsSagaState { CorrelationId = os.Id, CurrentState = "AguardandoPagamento", LinkPagamento = "https://pay" });

        var resp = await Uc().ObterAsync(os.Id);

        resp.Saga.Should().NotBeNull();
        resp.Saga!.Estado.Should().Be("AguardandoPagamento");
        resp.Saga.LinkPagamento.Should().Be("https://pay");
    }

    [Fact]
    public async Task Obter_inexistente_404_e_sem_saga_resumo_nulo()
    {
        var os = NovaOs();
        (await Uc().ObterAsync(os.Id)).Saga.Should().BeNull();

        await FluentActions.Awaiting(() => Uc().ObterAsync(Guid.NewGuid())).Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Entregar_so_finalizada()
    {
        var os = NovaOs();
        await FluentActions.Awaiting(() => Uc().EntregarAsync(os.Id)).Should().ThrowAsync<DomainException>();

        os.IncluirServico(Guid.NewGuid(), "S", Money.Brl(10m), 1);
        os.EnviarParaAprovacao();
        os.AprovarOrcamento();
        os.ConfirmarPagamento();
        os.Finalizar();
        var resp = await Uc().EntregarAsync(os.Id);
        resp.Status.Should().Be("Entregue");
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Listar_e_painel_mapeiam_resumos()
    {
        var os = OrdemDeServico.Abrir(Guid.NewGuid(), Guid.NewGuid());
        _repo.Setup(r => r.ListarAsync(0, 10, null, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { os });
        _repo.Setup(r => r.ListarPainelAsync(0, 10, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { os });

        (await Uc().ListarAsync(0, 10, null)).Single().Id.Should().Be(os.Id);
        (await Uc().ListarPainelAsync(0, 10)).Single().Id.Should().Be(os.Id);
    }

    [Fact]
    public async Task TempoMedio_sem_finalizadas_zera_e_com_finalizadas_calcula()
    {
        _repo.Setup(r => r.ListarFinalizadasNoIntervaloAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OrdemDeServico>());
        (await Uc().CalcularTempoMedioAsync(null, null)).Quantidade.Should().Be(0);

        var os = NovaOs();
        os.IncluirServico(Guid.NewGuid(), "S", Money.Brl(10m), 1);
        os.EnviarParaAprovacao();
        os.AprovarOrcamento();
        os.ConfirmarPagamento();
        os.Finalizar();
        _repo.Setup(r => r.ListarFinalizadasNoIntervaloAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { os });
        var resp = await Uc().CalcularTempoMedioAsync(DateTime.Now.AddDays(-1), DateTime.SpecifyKind(DateTime.UtcNow.AddDays(1), DateTimeKind.Unspecified));
        resp.Quantidade.Should().Be(1);
    }

    private OrdemDeServico OsEmExecucao(Guid servicoId, int qtd)
    {
        var os = NovaOs();
        os.IncluirServico(servicoId, "S", Money.Brl(10m), qtd);
        os.EnviarParaAprovacao();
        os.AprovarOrcamento();
        os.ConfirmarPagamento();
        return os;
    }

    [Fact]
    public async Task StatusPublico_em_execucao_estima_pelo_catalogo()
    {
        var servicoId = Guid.NewGuid();
        var os = OsEmExecucao(servicoId, 2);
        _catalogo.Setup(c => c.ObterServicoAsync(servicoId, It.IsAny<CancellationToken>())).ReturnsAsync(Item(servicoId, min: 30));

        var resp = await Uc().ObterStatusPublicoAsync(os.Id);

        resp.EstimativaConclusao.Should().Be(os.IniciadaExecucaoEm!.Value.AddMinutes(60));
    }

    [Fact]
    public async Task StatusPublico_catalogo_falha_estimativa_null()
    {
        var servicoId = Guid.NewGuid();
        var os = OsEmExecucao(servicoId, 1);
        _catalogo.Setup(c => c.ObterServicoAsync(servicoId, It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("401"));

        var resp = await Uc().ObterStatusPublicoAsync(os.Id);

        resp.Status.Should().Be("EmExecucao");
        resp.EstimativaConclusao.Should().BeNull();
    }

    [Fact]
    public async Task StatusPublico_timeout_do_catalogo_estimativa_null()
    {
        var servicoId = Guid.NewGuid();
        var os = OsEmExecucao(servicoId, 1);
        _catalogo.Setup(c => c.ObterServicoAsync(servicoId, It.IsAny<CancellationToken>())).ThrowsAsync(new TaskCanceledException("timeout do HttpClient"));

        (await Uc().ObterStatusPublicoAsync(os.Id)).EstimativaConclusao.Should().BeNull();
    }

    [Fact]
    public async Task StatusPublico_timeout_do_polly_estimativa_null()
    {
        var servicoId = Guid.NewGuid();
        var os = OsEmExecucao(servicoId, 1);
        _catalogo.Setup(c => c.ObterServicoAsync(servicoId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Polly.Timeout.TimeoutRejectedException("timeout da resiliência"));

        (await Uc().ObterStatusPublicoAsync(os.Id)).EstimativaConclusao.Should().BeNull();
    }

    [Fact]
    public async Task StatusPublico_cancelado_pelo_chamador_propaga()
    {
        var servicoId = Guid.NewGuid();
        var os = OsEmExecucao(servicoId, 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _catalogo.Setup(c => c.ObterServicoAsync(servicoId, It.IsAny<CancellationToken>())).ThrowsAsync(new TaskCanceledException());

        await FluentActions.Awaiting(() => Uc().ObterStatusPublicoAsync(os.Id, cts.Token)).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DefinirItens_item_nulo_no_array_400()
    {
        var os = NovaOs();

        await FluentActions.Awaiting(() => Uc().DefinirItensAsync(os.Id,
                new DefinirItensRequest(new ItemQtdRequest[] { null! }, Array.Empty<ItemQtdRequest>())))
            .Should().ThrowAsync<AppException>();
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
    [Fact]
    public async Task StatusPublico_fora_de_execucao_nao_consulta_catalogo_e_servico_removido_conta_zero()
    {
        var os = NovaOs();
        (await Uc().ObterStatusPublicoAsync(os.Id)).EstimativaConclusao.Should().BeNull();
        _catalogo.VerifyNoOtherCalls();

        var servicoId = Guid.NewGuid();
        var exec = OsEmExecucao(servicoId, 1);
        _catalogo.Setup(c => c.ObterServicoAsync(servicoId, It.IsAny<CancellationToken>())).ReturnsAsync((ItemCatalogo?)null);
        (await Uc().ObterStatusPublicoAsync(exec.Id)).EstimativaConclusao.Should().Be(exec.IniciadaExecucaoEm);
    }

    [Fact]
    public async Task Abrir_veiculo_de_outro_cliente_409_e_cliente_inexistente_404()
    {
        var cliente = Cliente.Cadastrar("Joao", Documento.Parse("11144477735"), "j@x.com", "1");
        var veiculo = Veiculo.Cadastrar(Guid.NewGuid(), Placa.Create("ABC1D23"), "VW", "Gol", 2020);
        _clientes.Setup(c => c.ObterPorIdAsync(cliente.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cliente);
        _veiculos.Setup(v => v.ObterPorIdAsync(veiculo.Id, It.IsAny<CancellationToken>())).ReturnsAsync(veiculo);

        await FluentActions.Awaiting(() => Uc().AbrirAsync(new AbrirOSRequest(cliente.Id, veiculo.Id))).Should().ThrowAsync<ConflictException>();
        await FluentActions.Awaiting(() => Uc().AbrirAsync(new AbrirOSRequest(Guid.NewGuid(), veiculo.Id))).Should().ThrowAsync<NotFoundException>();
        await FluentActions.Awaiting(() => Uc().AbrirAsync(new AbrirOSRequest(cliente.Id, Guid.NewGuid()))).Should().ThrowAsync<NotFoundException>();
        _bus.Verify(b => b.Publish(It.IsAny<OrdemServicoAberta>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}