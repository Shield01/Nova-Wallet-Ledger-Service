using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaWallet.Api.Auth;
using NovaWallet.Api.Contracts;
using NovaWallet.Api.Errors;
using NovaWallet.Api.Idempotency;
using NovaWallet.Application.Idempotency;
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
    [ProducesResponseType<TransferResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IResult> Transfer(TransferRequest request, CancellationToken cancellationToken)
    {
        var key = IdempotencyHttp.ReadKey(Request);
        if (IdempotentRequest.Validate(key) is { } invalidKey)
        {
            return invalidKey.ToProblem(HttpContext);
        }

        var outcome = await transfers.TransferAsync(request.SourceWalletId!.Value, request.DestinationWalletId!.Value, request.AmountKobo, key!, cancellationToken);
        return IdempotencyHttp.Render(outcome, HttpContext, result =>
            Results.Json(TransferResponse.From(result), statusCode: StatusCodes.Status201Created));
    }
}
