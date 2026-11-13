using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Os.Application.Catalogo;
using Os.Infrastructure.Catalogo;

namespace Os.Tests.Infrastructure;

public class CatalogoClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? Ultima { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Ultima = request;
            return Task.FromResult(responder(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (CatalogoClient Client, StubHandler Handler) Criar(Func<HttpRequestMessage, HttpResponseMessage> r)
    {
        var handler = new StubHandler(r);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://catalogo.test") };
        return (new CatalogoClient(http), handler);
    }

    [Fact]
    public async Task ObterServico_200_mapeia_campos_do_catalogo()
    {
        var id = Guid.NewGuid();
        var (client, handler) = Criar(_ => Json(HttpStatusCode.OK,
            $$"""{"id":"{{id}}","nome":"Troca de óleo","descricao":"x","valorBase":150.5,"tempoEstimadoMinutos":45,"ativo":true}"""));

        var item = await client.ObterServicoAsync(id, CancellationToken.None);

        item.Should().Be(new ItemCatalogo(id, "Troca de óleo", 150.5m, true, 45));
        handler.Ultima!.RequestUri!.AbsolutePath.Should().Be($"/api/servicos/{id}");
    }

    [Fact]
    public async Task ObterServico_inativo_preserva_flag()
    {
        var id = Guid.NewGuid();
        var (client, _) = Criar(_ => Json(HttpStatusCode.OK,
            $$"""{"id":"{{id}}","nome":"S","descricao":"x","valorBase":10,"tempoEstimadoMinutos":5,"ativo":false}"""));

        (await client.ObterServicoAsync(id, CancellationToken.None))!.Ativo.Should().BeFalse();
    }

    [Fact]
    public async Task ObterServico_404_retorna_null()
    {
        var (client, _) = Criar(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        (await client.ObterServicoAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task ObterServico_erro_lanca_HttpRequestException(HttpStatusCode status)
    {
        var (client, _) = Criar(_ => new HttpResponseMessage(status));

        var act = () => client.ObterServicoAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task ObterPeca_200_mapeia_campos_do_catalogo()
    {
        var id = Guid.NewGuid();
        var (client, handler) = Criar(_ => Json(HttpStatusCode.OK,
            $$"""{"id":"{{id}}","nome":"Filtro","codigo":"F1","unidade":"un","valor":35.9,"ativo":true,"quantidadeDisponivel":3,"quantidadeReservada":0,"limiteMinimo":1,"estoqueBaixo":false}"""));

        var item = await client.ObterPecaAsync(id, CancellationToken.None);

        item.Should().Be(new ItemCatalogo(id, "Filtro", 35.9m, true, 0));
        handler.Ultima!.RequestUri!.AbsolutePath.Should().Be($"/api/pecas/{id}");
    }

    [Fact]
    public async Task ObterPeca_404_retorna_null()
    {
        var (client, _) = Criar(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        (await client.ObterPecaAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ObterPeca_500_lanca_HttpRequestException()
    {
        var (client, _) = Criar(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var act = () => client.ObterPecaAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task RepassarJwtHandler_copia_Authorization_do_contexto()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Authorization = "Bearer abc.def";
        var inner = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new RepassarJwtHandler(new HttpContextAccessor { HttpContext = ctx }) { InnerHandler = inner };

        await new HttpClient(handler).GetAsync("http://catalogo.test/x");

        inner.Ultima!.Headers.Authorization!.ToString().Should().Be("Bearer abc.def");
    }

    [Fact]
    public async Task RepassarJwtHandler_sem_contexto_nao_adiciona_header()
    {
        var inner = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new RepassarJwtHandler(new HttpContextAccessor()) { InnerHandler = inner };

        await new HttpClient(handler).GetAsync("http://catalogo.test/x");

        inner.Ultima!.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public void AddCatalogo_registra_cliente_e_usa_BaseUrl_configurada()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Catalogo:BaseUrl"] = "http://exec:9" })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatalogo(cfg);

        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<ICatalogoClient>().Should().BeOfType<CatalogoClient>();
        CatalogoOptions.FromConfiguration(cfg).BaseUrl.Should().Be("http://exec:9");
        CatalogoOptions.FromConfiguration(new ConfigurationBuilder().Build()).BaseUrl.Should().Be("http://localhost:5003");
    }
}