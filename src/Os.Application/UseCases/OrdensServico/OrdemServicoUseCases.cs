using MassTransit;
using Polly.Timeout;
using Oficina.Contracts;
using Os.Application.Catalogo;
using Os.Application.Common;
using Os.Application.DTOs;
using Os.Application.Mapping;
using Os.Application.Saga;
using Os.Domain.Clientes;
using Os.Domain.Common;
using Os.Domain.OrdensServico;
using Os.Domain.ValueObjects;
using Os.Domain.Veiculos;

namespace Os.Application.UseCases.OrdensServico;

/// <summary>
/// Casos de uso da OS (API). Mudanças de status do fluxo de aprovação/pagamento/execução/cancelamento
/// não são feitas aqui: o caso de uso publica o evento e a Saga aplica a transição.
/// Toda publicação é seguida de <c>CommitAsync</c> (o outbox do EF só grava no commit).
/// </summary>
public sealed class OrdemServicoUseCases
{
    private readonly IOrdemDeServicoRepository _repo;
    private readonly IClienteRepository _clientes;
    private readonly IVeiculoRepository _veiculos;
    private readonly ICatalogoClient _catalogo;
    private readonly ISagaConsulta _saga;
    private readonly IPublishEndpoint _bus;
    private readonly IUnitOfWork _uow;

    public OrdemServicoUseCases(
        IOrdemDeServicoRepository repo,
        IClienteRepository clientes,
        IVeiculoRepository veiculos,
        ICatalogoClient catalogo,
        ISagaConsulta saga,
        IPublishEndpoint bus,
        IUnitOfWork uow)
    {
        _repo = repo;
        _clientes = clientes;
        _veiculos = veiculos;
        _catalogo = catalogo;
        _saga = saga;
        _bus = bus;
        _uow = uow;
    }

    public async Task<OSResponse> AbrirAsync(AbrirOSRequest req, CancellationToken ct = default)
    {
        var cliente = await _clientes.ObterPorIdAsync(req.ClienteId, ct)
            ?? throw new NotFoundException("Cliente", req.ClienteId);
        var veiculo = await _veiculos.ObterPorIdAsync(req.VeiculoId, ct)
            ?? throw new NotFoundException("Veículo", req.VeiculoId);
        if (veiculo.ClienteId != cliente.Id)
            throw new ConflictException("Veículo não pertence ao cliente informado.");

        var os = OrdemDeServico.Abrir(cliente.Id, veiculo.Id);
        await _repo.AdicionarAsync(os, ct);
        await _bus.Publish(new OrdemServicoAberta(os.Id, DateTime.UtcNow, os.ClienteId, os.VeiculoId), ct);
        await _uow.CommitAsync(ct);
        return os.ToResponse();
    }

    public async Task<OSResponse> ObterAsync(Guid id, CancellationToken ct = default)
    {
        var os = await Obter(id, ct);
        var saga = await _saga.ObterAsync(id, ct);
        return os.ToResponse(saga);
    }

    public async Task<IReadOnlyList<OSResumoResponse>> ListarAsync(int skip, int take, StatusOS? status, CancellationToken ct = default)
    {
        var list = await _repo.ListarAsync(skip, take, status, ct);
        return list.Select(o => o.ToResumo()).ToList();
    }

    /// <summary>
    /// Painel de acompanhamento: OS ativas ordenadas por prioridade de status
    /// (Em execução &gt; Aguardando pagamento &gt; Aguardando aprovação &gt; Em diagnóstico &gt; Recebida) e, no mesmo status,
    /// das mais antigas para as mais recentes. Finalizadas/Entregues/Canceladas ficam de fora.
    /// </summary>
    public async Task<IReadOnlyList<OSResumoResponse>> ListarPainelAsync(int skip, int take, CancellationToken ct = default)
    {
        var list = await _repo.ListarPainelAsync(skip, take, ct);
        return list.Select(o => o.ToResumo()).ToList();
    }

    /// <summary>
    /// Substitui os itens da OS. Nome e valor vêm sempre do catálogo (o request só traz id e quantidade);
    /// qualquer item inválido aborta antes de alterar a OS.
    /// </summary>
    public async Task<OSResponse> DefinirItensAsync(Guid osId, DefinirItensRequest req, CancellationToken ct = default)
    {
        var os = await Obter(osId, ct);
        var servicosReq = req.Servicos ?? Array.Empty<ItemQtdRequest>();
        var pecasReq = req.Pecas ?? Array.Empty<ItemQtdRequest>();
        foreach (var i in servicosReq.Concat(pecasReq))
        {
            if (i is null)
                throw new AppException("Item nulo na lista de itens.");
            if (i.Quantidade <= 0)
                throw new AppException($"Quantidade inválida para o item {i.Id}: deve ser maior que zero.");
        }

        var servicos = new List<(Guid, string, Money, int)>();
        foreach (var i in servicosReq)
        {
            var item = await _catalogo.ObterServicoAsync(i.Id, ct) ?? throw new NotFoundException("Serviço", i.Id);
            ValidarItem(item, "Serviço inativo não pode ser incluído na OS.");
            servicos.Add((item.Id, item.Nome, Money.Brl(item.Valor), i.Quantidade));
        }

        var pecas = new List<(Guid, string, Money, int)>();
        foreach (var i in pecasReq)
        {
            var item = await _catalogo.ObterPecaAsync(i.Id, ct) ?? throw new NotFoundException("Peça", i.Id);
            ValidarItem(item, "Peça inativa não pode ser incluída na OS.");
            pecas.Add((item.Id, item.Nome, Money.Brl(item.Valor), i.Quantidade));
        }

        os.SubstituirItens(servicos, pecas);
        await _uow.CommitAsync(ct);
        return os.ToResponse();
    }

    public async Task<OSResponse> IniciarDiagnosticoAsync(Guid osId, IniciarDiagnosticoRequest req, CancellationToken ct = default)
    {
        var os = await Obter(osId, ct);
        os.IniciarDiagnostico(req.Observacoes);
        await _uow.CommitAsync(ct);
        return os.ToResponse();
    }

    /// <summary>
    /// Só valida e publica <c>OrcamentoSolicitado</c>; o status não muda aqui — a Saga aplica
    /// <c>EnviarParaAprovacao</c> ao consumir o evento.
    /// </summary>
    public async Task<OSResponse> EnviarOrcamentoAsync(Guid osId, CancellationToken ct = default)
    {
        var os = await Obter(osId, ct);
        if (os.Status is not (StatusOS.Recebida or StatusOS.EmDiagnostico))
            throw new DomainException($"Transição inválida: status atual é {os.Status}, esperado Recebida/EmDiagnostico para solicitar orçamento.");
        if (os.Servicos.Count == 0 && os.Pecas.Count == 0)
            throw new DomainException("OS sem itens não pode ter orçamento solicitado.");

        var itens = os.Servicos
            .Select(s => new ItemOrcamento(TipoItem.Servico, s.ServicoId, s.Descricao, s.ValorUnitario.Amount, s.Quantidade))
            .Concat(os.Pecas.Select(p => new ItemOrcamento(TipoItem.Peca, p.PecaId, p.Descricao, p.ValorUnitario.Amount, p.Quantidade)))
            .ToList();

        await _bus.Publish(new OrcamentoSolicitado(os.Id, DateTime.UtcNow, os.ClienteId, itens, os.ValorTotal.Amount), ct);
        await _uow.CommitAsync(ct);
        return os.ToResponse();
    }

    public async Task<OSResponse> EntregarAsync(Guid osId, CancellationToken ct = default)
    {
        var os = await Obter(osId, ct);
        os.Entregar();
        await _uow.CommitAsync(ct);
        return os.ToResponse();
    }

    /// <summary>
    /// Pede o cancelamento à Saga (que muda o status e compensa). Sem instância de saga (OS recém-aberta,
    /// evento ainda não consumido) vale a regra do domínio; o <c>CancelamentoSolicitado</c> cria a instância.
    /// </summary>
    public async Task<OSResponse> CancelarAsync(Guid osId, CancelarOSRequest req, CancellationToken ct = default)
    {
        var os = await Obter(osId, ct);
        var saga = await _saga.ObterAsync(osId, ct);
        var permitido = os.PodeCancelar && (saga is null || EstadosSaga.Cancelaveis.Contains(saga.CurrentState));
        if (!permitido)
            throw new ConflictException($"OS no estado {saga?.CurrentState ?? os.Status.ToString()} não pode ser cancelada.");

        await _bus.Publish(new CancelamentoSolicitado(os.Id, DateTime.UtcNow, req.Motivo), ct);
        await _uow.CommitAsync(ct);
        return os.ToResponse(saga);
    }

    public async Task ReprocessarCompensacaoAsync(Guid osId, CancellationToken ct = default)
    {
        var saga = await _saga.ObterAsync(osId, ct);
        if (saga is null || saga.CurrentState != EstadosSaga.AguardandoAcaoManual)
            throw new ConflictException("Só é possível reprocessar a compensação de uma OS em AguardandoAcaoManual.");

        await _bus.Publish(new ReprocessarCompensacao(osId, DateTime.UtcNow), ct);
        await _uow.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<PendenciaResponse>> ListarPendenciasAsync(string estado = EstadosSaga.AguardandoAcaoManual, CancellationToken ct = default)
    {
        var sagas = await _saga.ListarPorEstadoAsync(estado, ct);
        return sagas.Select(s => new PendenciaResponse(s.CorrelationId, s.ToResumo())).ToList();
    }

    public async Task<TempoMedioExecucaoResponse> CalcularTempoMedioAsync(DateTime? de, DateTime? ate, CancellationToken ct = default)
    {
        var inicio = ToUtc(de) ?? DateTime.UtcNow.AddDays(-30);
        var fim = ToUtc(ate) ?? DateTime.UtcNow;
        var lista = await _repo.ListarFinalizadasNoIntervaloAsync(inicio, fim, ct);
        var tempos = lista
            .Where(o => o.TempoExecucao.HasValue)
            .Select(o => o.TempoExecucao!.Value.TotalMinutes)
            .ToList();
        if (tempos.Count == 0)
            return new TempoMedioExecucaoResponse(0, 0, 0);
        var media = tempos.Average();
        return new TempoMedioExecucaoResponse(tempos.Count, Math.Round(media, 2), Math.Round(media / 60, 2));
    }

    public async Task<StatusPublicoResponse> ObterStatusPublicoAsync(Guid id, CancellationToken ct = default)
    {
        var os = await Obter(id, ct);
        DateTime? estimativa = null;
        if (os.Status == StatusOS.EmExecucao)
        {
            try
            {
                // Estimativa simples: soma do tempo dos serviços (catálogo exige JWT; sem ele, sem estimativa).
                var totalMin = 0;
                foreach (var s in os.Servicos)
                {
                    var serv = await _catalogo.ObterServicoAsync(s.ServicoId, ct);
                    totalMin += serv is null ? 0 : serv.TempoEstimadoMinutos * s.Quantidade;
                }
                estimativa = os.IniciadaExecucaoEm?.AddMinutes(totalMin);
            }
            catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException
                                       || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                // Falha/timeout do catálogo (inclui o timeout do HttpClient e do Polly): sem estimativa.
                estimativa = null;
            }
        }
        return new StatusPublicoResponse(os.Id, os.Status.ToString(), os.CriadaEm, estimativa);
    }

    private async Task<OrdemDeServico> Obter(Guid id, CancellationToken ct) =>
        await _repo.ObterPorIdAsync(id, ct) ?? throw new NotFoundException("Ordem de Serviço", id);

    /// <summary>Item inativo → 409; nome vazio → resposta inválida do catálogo (nunca grava item sem descrição).</summary>
    private static void ValidarItem(ItemCatalogo item, string mensagemInativo)
    {
        if (!item.Ativo) throw new ConflictException(mensagemInativo);
        if (string.IsNullOrWhiteSpace(item.Nome))
            throw new HttpRequestException($"Resposta inválida do catálogo: item {item.Id} sem Nome.");
    }

    private static DateTime? ToUtc(DateTime? value) => value is null ? null : value.Value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.Value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
    };
}