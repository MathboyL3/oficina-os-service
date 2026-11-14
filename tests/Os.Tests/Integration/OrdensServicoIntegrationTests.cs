using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using MassTransit.Testing;
using Moq;
using Oficina.Contracts;
using Os.Application.Catalogo;
using Os.Application.DTOs;
using Os.Application.Saga;

namespace Os.Tests.Integration;

public class OrdensServicoIntegrationTests : IClassFixture<OsApiFactory>
{
    private readonly OsApiFactory _f;
    public OrdensServicoIntegrationTests(OsApiFactory f) => _f = f;

    private HttpClient Client(string role = "Gerente")
    {
        var c = _f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", OsApiFactory.Token(role));
        return c;
    }

    private static int _seq;

    /// <summary>CPF válido e único por chamada (o documento do cliente é único na base).</summary>
    private static string CpfNovo()
    {
        var d = (100000000 + Interlocked.Increment(ref _seq) * 7919).ToString()[..9].Select(c => c - '0').ToArray();
        int Dv(int[] x) { var s = 0; for (var i = 0; i < x.Length; i++) s += x[i] * (x.Length + 1 - i); var r = s % 11; return r < 2 ? 0 : 11 - r; }
        var d1 = Dv(d);
        var d2 = Dv(d.Append(d1).ToArray());
        return string.Concat(d) + d1 + d2;
    }

    private async Task<OSResponse> AbrirOsAsync(HttpClient c)
    {
        var cliente = await (await c.PostAsJsonAsync("/api/clientes",
            new CriarClienteRequest("Cliente OS", CpfNovo(), $"os-{Guid.NewGuid():N}@oficina.test", "11999999999")))
            .Content.ReadFromJsonAsync<ClienteResponse>();
        var placa = $"ABC{Interlocked.Increment(ref _seq) % 10000:D4}";
        var veiculo = await (await c.PostAsJsonAsync("/api/veiculos",
            new CadastrarVeiculoRequest(cliente!.Id, placa, "VW", "Gol", 2020))).Content.ReadFromJsonAsync<VeiculoResponse>();
        var resp = await c.PostAsJsonAsync("/api/ordens-servico", new AbrirOSRequest(cliente.Id, veiculo!.Id));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<OSResponse>())!;
    }

    [Fact]
    public async Task Fluxo_http_abrir_itens_enviar_publica_eventos()
    {
        var c = Client();
        var os = await AbrirOsAsync(c);
        (await _f.Harness.Published.Any<OrdemServicoAberta>(m => m.Context.Message.OrdemServicoId == os.Id)).Should().BeTrue();

        var servicoId = Guid.NewGuid();
        _f.Catalogo.Setup(x => x.ObterServicoAsync(servicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemCatalogo(servicoId, "Troca de óleo", 100m, true, 30));
        var itens = await c.PostAsJsonAsync($"/api/ordens-servico/{os.Id}/itens",
            new DefinirItensRequest(new[] { new ItemQtdRequest(servicoId, 2) }, Array.Empty<ItemQtdRequest>()));
        itens.StatusCode.Should().Be(HttpStatusCode.OK);
        (await itens.Content.ReadFromJsonAsync<OSResponse>())!.ValorTotal.Should().Be(200m);

        (await c.PostAsync($"/api/ordens-servico/{os.Id}/enviar-orcamento", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _f.Harness.Published.Any<OrcamentoSolicitado>(m => m.Context.Message.OrdemServicoId == os.Id)).Should().BeTrue();

        _f.Sagas.Semear(os.Id, "GerandoOrcamento");
        var detalhe = await c.GetFromJsonAsync<OSResponse>($"/api/ordens-servico/{os.Id}");
        detalhe!.Saga!.Estado.Should().Be("GerandoOrcamento");
    }

    [Fact]
    public async Task Itens_com_servico_inexistente_404()
    {
        var c = Client();
        var os = await AbrirOsAsync(c);
        var servicoId = Guid.NewGuid();
        _f.Catalogo.Setup(x => x.ObterServicoAsync(servicoId, It.IsAny<CancellationToken>())).ReturnsAsync((ItemCatalogo?)null);

        var r = await c.PostAsJsonAsync($"/api/ordens-servico/{os.Id}/itens",
            new DefinirItensRequest(new[] { new ItemQtdRequest(servicoId, 1) }, Array.Empty<ItemQtdRequest>()));

        r.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.GetFromJsonAsync<OSResponse>($"/api/ordens-servico/{os.Id}"))!.Servicos.Should().BeEmpty();
    }

    [Fact]
    public async Task Itens_com_quantidade_zero_400()
    {
        var c = Client();
        var os = await AbrirOsAsync(c);

        var r = await c.PostAsJsonAsync($"/api/ordens-servico/{os.Id}/itens",
            new DefinirItensRequest(new[] { new ItemQtdRequest(Guid.NewGuid(), 0) }, Array.Empty<ItemQtdRequest>()));

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cancelar_os_finalizada_409()
    {
        var c = Client();
        var os = await AbrirOsAsync(c);
        _f.Sagas.Semear(os.Id, EstadosSaga.Finalizada);

        var r = await c.PostAsJsonAsync($"/api/ordens-servico/{os.Id}/cancelar", new CancelarOSRequest("desistiu"));

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _f.Harness.Published.Any<CancelamentoSolicitado>(m => m.Context.Message.OrdemServicoId == os.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Admin_exige_Gerente()
    {
        (await Client("Atendente").GetAsync("/api/admin/sagas")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Client("Atendente").PostAsync($"/api/admin/ordens-servico/{Guid.NewGuid()}/reprocessar-compensacao", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _f.CreateClient().GetAsync("/api/admin/sagas")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Client("Gerente").GetAsync("/api/admin/sagas")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_reprocessa_compensacao_e_lista_pendencias()
    {
        var id = Guid.NewGuid();
        _f.Sagas.Semear(id, EstadosSaga.AguardandoAcaoManual);
        var c = Client("Gerente");

        (await c.GetFromJsonAsync<List<PendenciaResponse>>("/api/admin/sagas"))!.Should().Contain(p => p.OrdemServicoId == id);
        (await c.PostAsync($"/api/admin/ordens-servico/{id}/reprocessar-compensacao", null)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await _f.Harness.Published.Any<ReprocessarCompensacao>(m => m.Context.Message.OrdemServicoId == id)).Should().BeTrue();
    }

    [Fact]
    public async Task Publico_status_sem_token_200()
    {
        var os = await AbrirOsAsync(Client());

        var r = await _f.CreateClient().GetAsync($"/api/ordens-servico/publico/{os.Id}/status");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.Content.ReadFromJsonAsync<StatusPublicoResponse>())!.Status.Should().Be("Recebida");
    }

    [Fact]
    public async Task Rotas_removidas_da_fase3_nao_existem()
    {
        var c = Client();
        var id = Guid.NewGuid();
        (await c.PostAsync($"/api/ordens-servico/{id}/aprovar", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsync($"/api/ordens-servico/{id}/finalizar", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Health_live_e_swagger_na_raiz()
    {
        var c = _f.CreateClient();
        (await c.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await c.GetAsync("/swagger/v1/swagger.json");
        doc.StatusCode.Should().Be(HttpStatusCode.OK);
        (await doc.Content.ReadAsStringAsync()).Should().Contain("/api/ordens-servico");
    }
}