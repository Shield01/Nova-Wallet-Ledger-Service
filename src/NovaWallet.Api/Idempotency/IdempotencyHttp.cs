using NovaWallet.Api.Errors;
using NovaWallet.Application.Idempotency;

namespace NovaWallet.Api.Idempotency;

public static class IdempotencyHttp
{
    public const string KeyHeader = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";

    public static string? ReadKey(HttpRequest request) =>
        request.Headers.TryGetValue(KeyHeader, out var values) ? values.ToString() : null;

    /// <summary>
    /// Renders an outcome the same way on the first response and on every replay. Problem Details
    /// get this request's traceId, correlationId and instance, so only those differ on a replay.
    /// </summary>
    public static IResult Render<T>(IdempotentOutcome<T> outcome, HttpContext httpContext, Func<T, IResult> success)
    {
        if (outcome.Replayed)
        {
            httpContext.Response.Headers[ReplayedHeader] = "true";
        }

        return outcome.Result.IsSuccess
            ? success(outcome.Result.Value)
            : outcome.Result.Error.ToProblem(httpContext);
    }
}
