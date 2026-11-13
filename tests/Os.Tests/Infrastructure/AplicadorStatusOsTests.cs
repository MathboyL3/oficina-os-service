using FluentAssertions;
using Moq;
using Os.Application.Common;
using Os.Domain.OrdensServico;
using Os.Infrastructure.Saga;

namespace Os.Tests.Infrastructure;

public class AplicadorStatusOsTests
{
    [Fact]
    public async Task Aplica_a_acao_no_agregado_carregado()
    {
        var os = OrdemDeServico.Abrir(Guid.NewGuid(), Guid.NewGuid());
        var repo = new Mock<IOrdemDeServicoRepository>();
        repo.Setup(r => r.ObterPorIdAsync(os.Id, It.IsAny<CancellationToken>())).ReturnsAsync(os);

        await new AplicadorStatusOs(repo.Object).AplicarAsync(os.Id, "Cancelar", o => o.Cancelar("x"), CancellationToken.None);

        os.Status.Should().Be(StatusOS.Cancelada);
    }

    [Fact]
    public async Task Lanca_NotFound_quando_a_OS_nao_existe()
    {
        var repo = new Mock<IOrdemDeServicoRepository>();
        repo.Setup(r => r.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((OrdemDeServico?)null);

        var act = () => new AplicadorStatusOs(repo.Object).AplicarAsync(Guid.NewGuid(), "Cancelar", _ => { }, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}