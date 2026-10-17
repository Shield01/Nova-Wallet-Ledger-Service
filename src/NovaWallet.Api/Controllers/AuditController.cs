using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaWallet.Api.Auth;
using NovaWallet.Api.Contracts;
using NovaWallet.Api.Errors;
using NovaWallet.Application;
using NovaWallet.Application.Audit;

namespace NovaWallet.Api.Controllers;

[ApiController]
[Route("api/v1/audit")]
[Authorize(Policy = Scopes.AuditRead)]
public sealed class AuditController(AuditService audit) : ControllerBase
{
    /// <summary>The append-only audit trail of balance mutations, newest first.</summary>
    [HttpGet]
    [ProducesResponseType<AuditPageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IResult> List(
        [FromQuery] Guid? walletId,
        [FromQuery, Range(1, PageCursor.MaxLimit)] int? limit,
        [FromQuery] string? cursor,
        CancellationToken cancellationToken)
    {
        if (!PageCursor.TryDecode(cursor, out var after))
        {
            return Results.Problem(ValidationProblems.Build(HttpContext, new Dictionary<string, string[]>
            {
                ["cursor"] = ["Not a cursor returned by this endpoint."],
            }));
        }

        var result = await audit.ListAsync(walletId, after, limit ?? PageCursor.DefaultLimit, cancellationToken);
        return result.IsSuccess
            ? Results.Ok(AuditPageResponse.From(result.Value))
            : result.Error.ToProblem(HttpContext);
    }
}
