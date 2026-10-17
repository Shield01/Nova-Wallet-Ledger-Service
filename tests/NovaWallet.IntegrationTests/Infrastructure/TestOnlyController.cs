using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NovaWallet.Api.Errors;
using NovaWallet.Domain.Errors;
using NovaWallet.Infrastructure.Persistence;

namespace NovaWallet.IntegrationTests.Infrastructure;

/// <summary>Endpoints that exist only in the test host, to exercise cross-cutting behaviour.</summary>
[ApiController]
[Route("test")]
[AllowAnonymous]
public sealed class TestOnlyController : ControllerBase
{
    public const string SecretMessage = "secret-internal-detail-7f3a";

    [HttpGet("throw")]
    public IActionResult Throw() => throw new InvalidOperationException(SecretMessage);

    [HttpGet("refuse/{code}")]
    public IResult Refuse(string code) =>
        new Error(code, "Refused by the test endpoint.").With("example", 42).ToProblem(HttpContext);

    [HttpPost("validate")]
    public IActionResult Validate(ValidatedBody body) => Ok(body);

    [HttpGet("db")]
    public async Task<IActionResult> QueryDatabase([FromServices] LedgerDbContext db, CancellationToken cancellationToken)
    {
        var one = await db.Database.SqlQueryRaw<int>("SELECT 1 AS \"Value\"").SingleAsync(cancellationToken);
        return Ok(one);
    }

    public sealed class ValidatedBody
    {
        [Required]
        [StringLength(10, MinimumLength = 1)]
        public string? CustomerName { get; init; }

        [Range(1, long.MaxValue)]
        public long AmountKobo { get; init; }
    }
}
