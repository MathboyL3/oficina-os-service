using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Os.Application.Saga;
using Os.Domain.OrdensServico;
using Os.Domain.ValueObjects;
using Os.Infrastructure.Persistence;
using Os.Infrastructure.Persistence.Repositories;
using Os.Infrastructure.Saga;

namespace Os.Tests.Infrastructure;

public class SagaConsultaEPainelTests
{
    private static OsDbContext InMemory(string nome) =>
        new(new DbContextOptionsBuilder<OsDbContext>().UseInMemoryDatabase(nome).Options);

    private static OsSagaState Saga(Guid id, string estado, DateTime em) => new()
    {
        CorrelationId = id, CurrentState = estado, CriadaEm = em, AtualizadaEm = em
    };

    [Fact]
    public async Task Consulta_obtem_e_lista_por_estado()
    {
        var nome = Guid.NewGuid().ToString();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await using (var ctx = InMemory(nome))
        {
            ctx.Sagas.AddRange(
                Saga(a, EstadosSaga.AguardandoAcaoManual, DateTime.UtcNow),
                Saga(b, EstadosSaga.EmExecucao, DateTime.UtcNow));
            await ctx.SaveChangesAsync();
        }

        await using var leitura = InMemory(nome);
        var consulta = new SagaConsulta(leitura);

        (await consulta.ObterAsync(a, default))!.CurrentState.Should().Be("AguardandoAcaoManual");
        (await consulta.ObterAsync(Guid.NewGuid(), default)).Should().BeNull();
        (await consulta.ListarPorEstadoAsync(EstadosSaga.AguardandoAcaoManual, default)).Select(s => s.CorrelationId)
            .Should().Equal(a);
        leitura.ChangeTracker.Entries<OsSagaState>().Should().BeEmpty(); // AsNoTracking
    }

    private static OrdemDeServico OsEm(StatusOS alvo, DateTime criadaEm)
    {
        var os = OrdemDeServico.Abrir(Guid.NewGuid(), Guid.NewGuid());
        typeof(OrdemDeServico).GetProperty(nameof(OrdemDeServico.CriadaEm))!.SetValue(os, criadaEm);
        if (alvo == StatusOS.Recebida) return os;
        os.IncluirServico(Guid.NewGuid(), "S", Money.Brl(10m), 1);
        if (alvo == StatusOS.EmDiagnostico) { os.IniciarDiagnostico(); return os; }
        os.EnviarParaAprovacao();
        if (alvo == StatusOS.AguardandoAprovacao) return os;
        os.AprovarOrcamento();
        if (alvo == StatusOS.AguardandoPagamento) return os;
        os.ConfirmarPagamento();
        if (alvo == StatusOS.EmExecucao) return os;
        os.Finalizar();
        return os;
    }

    [Fact]
    public async Task Painel_ordena_EmExecucao_AguardandoPagamento_AguardandoAprovacao_EmDiagnostico_Recebida()
    {
        var nome = Guid.NewGuid().ToString();
        var t = DateTime.UtcNow.AddHours(-10);
        var recebida = OsEm(StatusOS.Recebida, t);
        var diag = OsEm(StatusOS.EmDiagnostico, t.AddMinutes(1));
        var aprov = OsEm(StatusOS.AguardandoAprovacao, t.AddMinutes(2));
        var pagto = OsEm(StatusOS.AguardandoPagamento, t.AddMinutes(3));
        var exec = OsEm(StatusOS.EmExecucao, t.AddMinutes(4));
        var finalizada = OsEm(StatusOS.Finalizada, t.AddMinutes(5));
        await using (var ctx = InMemory(nome))
        {
            ctx.OrdensServico.AddRange(recebida, diag, aprov, pagto, exec, finalizada);
            await ctx.SaveChangesAsync();
        }

        await using var leitura = InMemory(nome);
        var uc = new Os.Application.UseCases.OrdensServico.OrdemServicoUseCases(
            new OrdemDeServicoRepository(leitura), null!, null!, null!, null!, null!, null!);

        var painel = await uc.ListarPainelAsync(0, 10);

        painel.Select(o => o.Id).Should().Equal(exec.Id, pagto.Id, aprov.Id, diag.Id, recebida.Id);
    }
}