using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaWallet.Api.Auth;
using NovaWallet.Api.Contracts;
using NovaWallet.Api.Errors;
using NovaWallet.Api.Idempotency;
using NovaWallet.Application.Ledger;

namespace NovaWallet.Api.Controllers;

[ApiController]
[Route("api/v1/wallets/{walletId:guid}/credits")]
[Authorize(Policy = Scopes.LedgerCredit)]
public sealed class CreditsController(CreditService credits) : ControllerBase
{
    /// <summary>
    /// Credits a wallet from the NIP inbound settlement account (simulated inbound transfer).
    /// Called by the inbound-transfer system, never by customers.
    /// </summary>
    [HttpPost]
    [RequiresIdempotencyKey]
    [ProducesResponseType<CreditResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IResult> Credit(Guid walletId, CreditRequest request, CancellationToken cancellationToken)
    {
        if (!IdempotencyHttp.TryReadKey(Request, out var key, out var invalidKey))
        {
            return invalidKey!.ToProblem(HttpContext);
        }

        var outcome = await credits.CreditAsync(walletId, request.AmountKobo, key, cancellationToken);
        return IdempotencyHttp.Render(outcome, HttpContext, result =>
            Results.Json(CreditResponse.From(result), statusCode: StatusCodes.Status201Created));
    }
}
