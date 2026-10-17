using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaWallet.Application.Abstractions;

namespace NovaWallet.IntegrationTests.Infrastructure;

/// <summary>Test-only endpoints protected by each policy, to exercise authentication and authorisation.</summary>
[ApiController]
[Route("test/auth")]
public sealed class TestAuthController(ICallerContext caller) : ControllerBase
{
    [HttpGet("any")]
    public IActionResult Any() => Ok(new { subject = caller.Subject });

    [HttpGet("wallet")]
    [Authorize(Policy = "wallet")]
    public IActionResult Wallet() => Ok(new { subject = caller.Subject });

    [HttpGet("audit")]
    [Authorize(Policy = "audit:read")]
    public IActionResult Audit() => Ok(new { subject = caller.Subject });

    [HttpGet("credit")]
    [Authorize(Policy = "ledger:credit")]
    public IActionResult Credit() => Ok(new { subject = caller.Subject });
}
