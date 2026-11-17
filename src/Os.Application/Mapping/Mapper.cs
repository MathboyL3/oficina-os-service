using Os.Application.DTOs;
using Os.Application.Saga;
using Os.Domain.Clientes;
using Os.Domain.Identidade;
using Os.Domain.OrdensServico;
using Os.Domain.Veiculos;

namespace Os.Application.Mapping;

public static class Mapper
{
    public static ClienteResponse ToResponse(this Cliente c) => new(
        c.Id, c.Nome, c.Documento.Numero, c.Documento.Tipo.ToString(),
        c.Email, c.Telefone, c.CriadoEm);

    public static VeiculoResponse ToResponse(this Veiculo v) => new(
        v.Id, v.ClienteId, v.Placa.Formatada(), v.Marca, v.Modelo, v.Ano);

    public static ItemServicoResponse ToResponse(this ItemServico i) => new(
        i.Id, i.ServicoId, i.Descricao, i.ValorUnitario.Amount, i.Quantidade, i.Subtotal.Amount);

    public static ItemPecaResponse ToResponse(this ItemPeca i) => new(
        i.Id, i.PecaId, i.Descricao, i.ValorUnitario.Amount, i.Quantidade, i.Subtotal.Amount);

    public static HistoricoStatusResponse ToResponse(this HistoricoStatus h) => new(
        h.De.ToString(), h.Para.ToString(), h.OcorreuEm, h.Observacao);

    public static SagaResumo ToResumo(this OsSagaState s) => new(
        s.CurrentState, s.OrcamentoId, s.PagamentoId, s.LinkPagamento, s.MotivoCancelamento);

    public static OSResponse ToResponse(this OrdemDeServico o, OsSagaState? saga = null) => new(
        o.Id, o.ClienteId, o.VeiculoId, o.Status.ToString(), o.ValorTotal.Amount,
        o.CriadaEm, o.IniciadaExecucaoEm, o.FinalizadaEm, o.EntregueEm,
        o.ObservacoesDiagnostico,
        o.Servicos.Select(ToResponse).ToList(),
        o.Pecas.Select(ToResponse).ToList(),
        o.Historico.Select(ToResponse).ToList(),
        saga?.ToResumo());

    public static OSResumoResponse ToResumo(this OrdemDeServico o) => new(
        o.Id, o.ClienteId, o.VeiculoId, o.Status.ToString(), o.ValorTotal.Amount, o.CriadaEm);

    public static UsuarioResponse ToResponse(this Usuario u) => new(
        u.Id, u.Nome, u.Email, u.Role.ToString());
}