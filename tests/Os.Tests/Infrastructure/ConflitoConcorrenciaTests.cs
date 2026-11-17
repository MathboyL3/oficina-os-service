using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Os.Infrastructure.Messaging;

namespace Os.Tests.Infrastructure;

public class ConflitoConcorrenciaTests
{
    [Fact]
    public void Reconhece_DbUpdateConcurrencyException()
        => ConflitoConcorrencia.EhRetentavel(new DbUpdateConcurrencyException("x")).Should().BeTrue();

    [Fact]
    public void Reconhece_conflito_encadeado_em_InnerException()
        => ConflitoConcorrencia.EhRetentavel(new InvalidOperationException("x", new DbUpdateConcurrencyException("y"))).Should().BeTrue();

    [Theory]
    [InlineData("40001")] // serialization_failure (outbox EF usa transação Serializable)
    [InlineData("40P01")] // deadlock_detected
    public void Reconhece_falha_de_serializacao_do_Postgres_encadeada(string sqlState)
    {
        var pg = new PostgresException("msg", "ERROR", "ERROR", sqlState);
        var ex = new InvalidOperationException("transient", new DbUpdateException("save", pg));
        ConflitoConcorrencia.EhRetentavel(ex).Should().BeTrue();
    }

    [Fact]
    public void Reconhece_insert_concorrente_da_mesma_saga()
    {
        // OrdemServicoAberta e OrcamentoSolicitado têm InsertOnInitial: se chegam juntos, o segundo
        // INSERT colide na PK da saga. No retry a linha já existe e a mensagem é processada normalmente.
        var pg = Unique("PK_os_saga_state");
        var ex = new DbUpdateException("save", pg);
        ConflitoConcorrencia.EhRetentavel(ex).Should().BeTrue();
    }

    [Fact]
    public void Nao_reconhece_unique_violation_de_outra_constraint()
        => ConflitoConcorrencia.EhRetentavel(new DbUpdateException("save", Unique("IX_clientes_documento"))).Should().BeFalse();

    private static PostgresException Unique(string constraint)
        => new("duplicate key", "ERROR", "ERROR", "23505", tableName: "t", constraintName: constraint);

    [Fact]
    public void Nao_reconhece_outros_erros()
    {
        ConflitoConcorrencia.EhRetentavel(new InvalidOperationException("x")).Should().BeFalse();
        ConflitoConcorrencia.EhRetentavel(new PostgresException("m", "ERROR", "ERROR", "23505")).Should().BeFalse();
    }
}