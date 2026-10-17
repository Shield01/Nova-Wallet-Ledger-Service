using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using NovaWallet.Domain.Errors;
using Npgsql;

namespace NovaWallet.Api.Errors;

/// <summary>
/// Last-resort handler: logs the exception and returns a Problem Details body that reveals
/// nothing about the failure beyond its category.
/// </summary>
public sealed partial class UnhandledExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<UnhandledExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var code = IsDatabaseUnavailable(exception) ? ErrorCodes.ServiceUnavailable : ErrorCodes.InternalError;
        LogUnhandled(logger, exception, code);

        var status = ErrorCatalogue.StatusFor(code);
        httpContext.Response.StatusCode = status;
        var problem = new ProblemDetails { Status = status };
        problem.Extensions[ProblemDetailsSetup.CodeExtension] = code;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = null,
        });
    }

    private static bool IsDatabaseUnavailable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is NpgsqlException { IsTransient: true } or System.Net.Sockets.SocketException or TimeoutException)
            {
                return true;
            }
        }

        return false;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception mapped to {Code}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string code);
}
