using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaWallet.Api.Auth;
using NovaWallet.Api.Contracts;
using NovaWallet.Api.Errors;
using NovaWallet.Application.Wallets;

namespace NovaWallet.Api.Controllers;

[ApiController]
[Route("api/v1/wallets")]
[Authorize(Policy = Scopes.Wallet)]
public sealed class WalletsController(WalletService wallets) : ControllerBase
{
    /// <summary>Opens the caller's NGN wallet with a zero balance.</summary>
    [HttpPost]
    [ProducesResponseType<WalletResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IResult> Create(CreateWalletRequest request, CancellationToken cancellationToken)
    {
        // [Required] on the request guarantees a value once model validation has passed.
        var result = await wallets.CreateAsync(request.CustomerId!, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Error.ToProblem(HttpContext);
        }

        var wallet = WalletResponse.From(result.Value);
        return Results.Created($"/api/v1/wallets/{wallet.WalletId}/balance", wallet);
    }

    /// <summary>Current balance of one of the caller's wallets, in kobo.</summary>
    [HttpGet("{walletId:guid}/balance")]
    [ProducesResponseType<BalanceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IResult> GetBalance(Guid walletId, CancellationToken cancellationToken)
    {
        var result = await wallets.GetBalanceAsync(walletId, cancellationToken);
        return result.IsSuccess
            ? Results.Ok(new BalanceResponse(result.Value.WalletId, result.Value.BalanceKobo, result.Value.Currency, result.Value.AsOf))
            : result.Error.ToProblem(HttpContext);
    }
}
