using FluentAssertions;
using Os.Api.Security;

namespace Os.Tests.Api;

public sealed class MascaraPathEnricherTests
{
    [Theory]
    [InlineData("/internal/clientes/por-documento/52998224725", "/internal/clientes/por-documento/{valor}")]
    [InlineData("/api/clientes/por-documento/52998224725", "/api/clientes/por-documento/{valor}")]
    [InlineData("/API/Clientes/Por-Documento/529.982.247-25/", "/API/Clientes/Por-Documento/{valor}")]
    public void Mascarar_PrefixoSensivel_SubstituiUltimoSegmento(string path, string esperado) =>
        MascaraPathEnricher.Mascarar(path).Should().Be(esperado);

    [Theory]
    [InlineData("/api/clientes")]
    [InlineData("/api/clientes/3fa85f64-5717-4562-b3fc-2c963f66afa6")]
    [InlineData("/api/ordens-servico/por-documento/123")]
    [InlineData("/health")]
    public void Mascarar_RotaNaoSensivel_Inalterada(string path) =>
        MascaraPathEnricher.Mascarar(path).Should().Be(path);
}