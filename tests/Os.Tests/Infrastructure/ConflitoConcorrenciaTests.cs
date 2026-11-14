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
    public void Nao_reconhece_outros_erros()
    {
        ConflitoConcorrencia.EhRetentavel(new InvalidOperationException("x")).Should().BeFalse();
        ConflitoConcorrencia.EhRetentavel(new PostgresException("m", "ERROR", "ERROR", "23505")).Should().BeFalse();
    }
}