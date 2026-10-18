using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NovaWallet.Api.Auth;
using NovaWallet.Api.Contracts;
using NovaWallet.Api.Errors;
using NovaWallet.Api.Idempotency;
using NovaWallet.Api.RateLimiting;
using NovaWallet.Application.Ledger;

namespace NovaWallet.Api.Controllers;

[ApiController]
[Route("api/v1/transfers")]
[Authorize(Policy = Scopes.Wallet)]
public sealed class TransfersController(TransferService transfers) : ControllerBase
{
    /// <summary>
    /// Moves money from one of the caller's wallets to another wallet. Retry a timed-out or failed
    /// request with the same Idempotency-Key; use a new key only for a new payment.
    /// </summary>
    [HttpPost]
    [RequiresIdempotencyKey]
    [EnableRateLimiting(TransferRateLimiting.PolicyName)]
    [ProducesResponseType<TransferResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IResult> Transfer(TransferRequest request, CancellationToken cancellationToken)
    {
        if (!IdempotencyHttp.TryReadKey(Request, out var key, out var invalidKey))
        {
            return invalidKey!.ToProblem(HttpContext);
        }

        // [Required] guarantees both ids once model validation has passed.
        var outcome = await transfers.TransferAsync(request.SourceWalletId!.Value, request.DestinationWalletId!.Value, request.AmountKobo, key, cancellationToken);
        return IdempotencyHttp.Render(outcome, HttpContext, result =>
            Results.Json(TransferResponse.From(result), statusCode: StatusCodes.Status201Created));
    }
}
