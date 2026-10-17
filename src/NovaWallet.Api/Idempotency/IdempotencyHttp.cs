using NovaWallet.Api.Errors;
using NovaWallet.Application.Idempotency;
using NovaWallet.Domain.Errors;

namespace NovaWallet.Api.Idempotency;

public static class IdempotencyHttp
{
    public const string KeyHeader = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";

    /// <summary>
    /// Reads and validates the Idempotency-Key header. Exactly one header is accepted: repeated
    /// headers would otherwise be joined into a different key.
    /// </summary>
    public static bool TryReadKey(HttpRequest request, out string key, out Error? error)
    {
        key = string.Empty;
        var values = request.Headers[KeyHeader];
        if (values.Count > 1)
        {
            error = new Error(ErrorCodes.IdempotencyKeyInvalid, "Send exactly one Idempotency-Key header.");
            return false;
        }

        error = IdempotentRequest.Validate(values.ToString());
        if (error is not null)
        {
            return false;
        }

        key = values.ToString();
        return true;
    }

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
