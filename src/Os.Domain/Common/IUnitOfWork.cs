namespace Os.Domain.Common;

public interface IUnitOfWork
{
    Task<int> CommitAsync(CancellationToken ct = default);

    /// <summary>
    /// Executa <paramref name="acao"/> serializada entre réplicas/conexões pela <paramref name="chave"/>
    /// (advisory lock transacional do Postgres, liberado no commit/rollback). Em provider não relacional é só a chamada direta.
    /// </summary>
    Task<T> ExecutarSerializadoAsync<T>(long chave, Func<CancellationToken, Task<T>> acao, CancellationToken ct = default);
}