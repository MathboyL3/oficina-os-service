using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Os.Infrastructure.Messaging;

/// <summary>
/// Falhas transitórias de concorrência que o retry da saga deve tentar de novo: conflito otimista do EF
/// (xmin da OS / Version da saga) e, no Postgres, serialization_failure (40001) / deadlock (40P01) — o outbox EF
/// abre transação Serializable, então duas mensagens da mesma OS processadas em paralelo colidem com 40001.
/// </summary>
public static class ConflitoConcorrencia
{
    public static bool EhRetentavel(Exception? e)
    {
        for (; e is not null; e = e.InnerException)
        {
            if (e is DbUpdateConcurrencyException) return true;
            if (e is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected }) return true;
        }
        return false;
    }
}