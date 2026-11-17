using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Os.Application.Saga;
using Os.Domain.Clientes;
using Os.Domain.OrdensServico;
using Os.Domain.ValueObjects;
using Os.Infrastructure.Persistence;
using Os.Infrastructure.Persistence.Repositories;

namespace Os.Tests.Infrastructure;

public class OsDbContextTests
{
    private static OsDbContext InMemory(string nome) =>
        new(new DbContextOptionsBuilder<OsDbContext>().UseInMemoryDatabase(nome).Options);

    private static OsDbContext Npgsql() => new OsDbContextFactory().CreateDbContext(Array.Empty<string>());

    [Fact]
    public async Task Persiste_e_relê_estado_da_saga_e_da_os()
    {
        var nome = Guid.NewGuid().ToString();
        var os = OrdemDeServico.Abrir(Guid.NewGuid(), Guid.NewGuid());
        os.IncluirServico(Guid.NewGuid(), "Troca de óleo", Money.Brl(100m), 2);
        var saga = new OsSagaState
        {
            CorrelationId = os.Id,
            CurrentState = "AguardandoPagamento",
            ClienteId = os.ClienteId,
            ValorTotal = 200m,
            ItensJson = "[{\"a\":1}]",
            OrcamentoId = Guid.NewGuid(),
            PagamentoId = Guid.NewGuid(),
            LinkPagamento = "https://pay/x",
            PecasReservadas = true,
            MotivoCancelamento = "x",
            CriadaEm = DateTime.UtcNow,
            AtualizadaEm = DateTime.UtcNow
        };

        await using (var ctx = InMemory(nome))
        {
            ctx.OrdensServico.Add(os);
            ctx.Sagas.Add(saga);
            await ctx.SaveChangesAsync();
        }

        await using var leitura = InMemory(nome);
        var osLida = await new OrdemDeServicoRepository(leitura).ObterPorIdAsync(os.Id);
        osLida!.ValorTotal.Amount.Should().Be(200m);
        osLida.Servicos.Should().HaveCount(1);
        osLida.Historico.Should().NotBeEmpty();

        var sagaLida = await leitura.Sagas.SingleAsync(s => s.CorrelationId == os.Id);
        sagaLida.CurrentState.Should().Be("AguardandoPagamento");
        sagaLida.ValorTotal.Should().Be(200m);
        sagaLida.ItensJson.Should().Be("[{\"a\":1}]");
        sagaLida.LinkPagamento.Should().Be("https://pay/x");
        sagaLida.PecasReservadas.Should().BeTrue();
        sagaLida.OrcamentoId.Should().Be(saga.OrcamentoId);
    }

    [Fact]
    public async Task Repositorios_e_unit_of_work_persistem_cliente_veiculo_e_usuario()
    {
        var nome = Guid.NewGuid().ToString();
        await using var ctx = InMemory(nome);
        var uow = new UnitOfWork(ctx);
        var clientes = new ClienteRepository(ctx);
        var cliente = Cliente.Cadastrar("Ana", Documento.Parse("52998224725"), "ana@x.com", "11999999999");
        var veiculo = Os.Domain.Veiculos.Veiculo.Cadastrar(cliente.Id, Placa.Create("ABC1D23"), "VW", "Gol", 2020);
        var usuario = Os.Domain.Identidade.Usuario.Criar("Bob", "bob@x.com", "hash", Os.Domain.Identidade.Role.Atendente);

        await clientes.AdicionarAsync(cliente);
        await new VeiculoRepository(ctx).AdicionarAsync(veiculo);
        await new UsuarioRepository(ctx).AdicionarAsync(usuario);
        (await uow.CommitAsync()).Should().BeGreaterThan(0);

        (await clientes.ObterPorIdAsync(cliente.Id))!.Nome.Should().Be("Ana");
        (await clientes.ObterPorDocumentoAsync(cliente.Documento))!.Id.Should().Be(cliente.Id);
        (await clientes.ContarAsync()).Should().Be(1);
        (await clientes.ListarAsync(0, 10)).Should().HaveCount(1);
        (await new VeiculoRepository(ctx).ObterPorPlacaAsync(veiculo.Placa))!.Id.Should().Be(veiculo.Id);
        (await new VeiculoRepository(ctx).ListarPorClienteAsync(cliente.Id)).Should().HaveCount(1);
        (await new UsuarioRepository(ctx).ObterPorEmailAsync("bob@x.com"))!.Id.Should().Be(usuario.Id);

        await clientes.RemoverAsync(cliente);
        await uow.CommitAsync();
        (await clientes.ContarAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Repositorio_de_os_lista_filtra_e_conta()
    {
        await using var ctx = InMemory(Guid.NewGuid().ToString());
        var cliente = Guid.NewGuid();
        var a = OrdemDeServico.Abrir(cliente, Guid.NewGuid());
        var b = OrdemDeServico.Abrir(cliente, Guid.NewGuid());
        b.Cancelar("teste");
        var repo = new OrdemDeServicoRepository(ctx);
        await repo.AdicionarAsync(a);
        await repo.AdicionarAsync(b);
        await ctx.SaveChangesAsync();

        (await repo.ListarAsync(0, 10, null)).Should().HaveCount(2);
        (await repo.ListarAsync(0, 10, StatusOS.Cancelada)).Should().ContainSingle();
        (await repo.ListarPainelAsync(0, 10)).Should().ContainSingle();
        (await repo.ListarPorClienteAsync(cliente)).Should().HaveCount(2);
        (await repo.ListarFinalizadasNoIntervaloAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1))).Should().BeEmpty();
        (await repo.ContarAsync(null)).Should().Be(2);
        (await repo.ContarAsync(StatusOS.Recebida)).Should().Be(1);
    }

    [Theory]
    [InlineData(typeof(Cliente), "clientes")]
    [InlineData(typeof(Os.Domain.Veiculos.Veiculo), "veiculos")]
    [InlineData(typeof(OrdemDeServico), "ordens_servico")]
    [InlineData(typeof(HistoricoStatus), "os_historico_status")]
    [InlineData(typeof(Os.Domain.Identidade.Usuario), "usuarios")]
    [InlineData(typeof(OsSagaState), "os_saga_state")]
    public void Modelo_mapeia_tabelas(Type entidade, string tabela)
        => Npgsql().Model.FindEntityType(entidade)!.GetTableName().Should().Be(tabela);

    [Fact]
    public void Clientes_mantem_colunas_da_fase3_lidas_pela_auth_function()
    {
        var et = Npgsql().Model.FindEntityType(typeof(Cliente))!;
        var tabela = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table("clientes");
        var colunas = et.GetProperties().Concat(et.GetNavigations().Select(n => n.TargetEntityType).SelectMany(t => t.GetProperties()))
            .Select(p => p.GetColumnName(tabela)).ToList();

        colunas.Should().Contain(new[] { "Id", "Nome", "Email", "documento_numero", "documento_tipo" });
    }

    [Fact]
    public void Modelo_inclui_outbox_inbox_e_saga_com_jsonb()
    {
        var model = Npgsql().Model;
        model.GetEntityTypes().Select(e => e.GetTableName()).Should().Contain(new[] { "InboxState", "OutboxMessage", "OutboxState" });
        model.FindEntityType(typeof(OsSagaState))!.FindProperty(nameof(OsSagaState.ItensJson))!
            .GetColumnType().Should().Be("jsonb");
    }

    [Fact]
    public void Migration_quartz_le_o_script_embutido_e_cria_as_tabelas_qrtz()
    {
        var migration = new Os.Infrastructure.Persistence.Migrations.Quartz();
        var up = typeof(Os.Infrastructure.Persistence.Migrations.Quartz)
            .GetMethod("Up", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var builder = new Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");

        up.Invoke(migration, new object[] { builder });

        builder.Operations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>()
            .Single().Sql.Should().Contain("CREATE TABLE qrtz_job_details").And.NotContain("DROP TABLE");
    }
}