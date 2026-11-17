using System.Text.Json;
using Os.Application.Common;
using Os.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Os.Api.Middleware;

public sealed class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _log;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (NotFoundException ex)
        {
            await Write(ctx, StatusCodes.Status404NotFound, ex.Message);
        }
        catch (ConflictException ex)
        {
            await Write(ctx, StatusCodes.Status409Conflict, ex.Message);
        }
        catch (DomainException ex)
        {
            await Write(ctx, StatusCodes.Status422UnprocessableEntity, ex.Message);
        }
        catch (ForbiddenException ex)
        {
            await Write(ctx, StatusCodes.Status403Forbidden, ex.Message);
        }
        catch (AppException ex)
        {
            await Write(ctx, StatusCodes.Status400BadRequest, ex.Message);
        }
        // Conflito de concorrência otimista (xmin da OS / Version da saga) ou violação de índice único
        // (ex.: e-mail ou documento já cadastrado): a requisição é repetível, não é erro interno.
        catch (DbUpdateConcurrencyException ex)
        {
            _log.LogWarning(ex, "Conflito de concorrência ao salvar");
            await Write(ctx, StatusCodes.Status409Conflict, "O registro foi alterado por outra operação. Tente novamente.");
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _log.LogWarning(ex, "Violação de unicidade ao salvar");
            await Write(ctx, StatusCodes.Status409Conflict, "Já existe um registro com os mesmos dados.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Erro não tratado");
            await Write(ctx, StatusCodes.Status500InternalServerError, "Erro interno do servidor.");
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static async Task Write(HttpContext ctx, int status, string message)
    {
        ctx.Response.Clear();
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        var body = JsonSerializer.Serialize(new { status, message });
        await ctx.Response.WriteAsync(body);
    }
}
