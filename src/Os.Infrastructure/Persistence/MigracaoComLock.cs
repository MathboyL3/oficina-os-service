using Microsoft.EntityFrameworkCore;

namespace Os.Infrastructure.Persistence;

/// <summary>
/// Aplica migrations serializadas por advisory lock do Postgres, para que varias replicas
/// subindo ao mesmo tempo nao corram entre si. Lock e migracao usam a mesma conexao (sessao).
/// </summary>
public static class MigracaoComLock
{
    // Constante arbitraria, exclusiva deste servico.
    public const long ChaveLock = 7301001;

    public static void MigrateComLock(this DbContext ctx)
    {
        var conexao = ctx.Database.GetDbConnection();
        var abriu = conexao.State != System.Data.ConnectionState.Open;
        if (abriu) conexao.Open();
        try
        {
            Executar(conexao, $"SELECT pg_advisory_lock({ChaveLock})");
            try
            {
                ctx.Database.Migrate();
            }
            finally
            {
                Executar(conexao, $"SELECT pg_advisory_unlock({ChaveLock})");
            }
        }
        finally
        {
            if (abriu) conexao.Close();
        }
    }

    private static void Executar(System.Data.Common.DbConnection conexao, string sql)
    {
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteScalar();
    }
}