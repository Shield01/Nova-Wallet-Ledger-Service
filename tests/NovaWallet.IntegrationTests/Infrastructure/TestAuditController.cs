using Microsoft.AspNetCore.Mvc;
using NovaWallet.Application.Abstractions;
using NovaWallet.Application.Audit;
using NovaWallet.Domain.Audit;
using NovaWallet.Infrastructure.Persistence;

namespace NovaWallet.IntegrationTests.Infrastructure;

/// <summary>
/// Seeds audit records through the real port inside a real unit of work, during a real request,
/// so actor and correlation id come from the request exactly as they will for money movements.
/// </summary>
[ApiController]
[Route("test/audit")]
public sealed class TestAuditController(
    IUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    LedgerDbContext db,
    ICallerContext caller,
    ICorrelationContext correlation,
    TimeProvider clock) : ControllerBase
{
    public const string FailureMessage = "Simulated failure after audit records were appended.";

    [HttpPost("seed")]
    public async Task<IActionResult> Seed(SeedRequest request, CancellationToken cancellationToken)
    {
        await unitOfWork.InTransactionAsync(_ =>
        {
            auditTrail.Append(Records(request).ToArray());
            return Task.FromResult(true);
        }, cancellationToken);
        return NoContent();
    }

    [HttpPost("seed-then-fail")]
    public async Task<IActionResult> SeedThenFail(SeedRequest request, CancellationToken cancellationToken)
    {
        await unitOfWork.InTransactionAsync<bool>(async ct =>
        {
            auditTrail.Append(Records(request).ToArray());
            await Task.Yield();
            throw new InvalidOperationException(FailureMessage);
        }, cancellationToken);
        return NoContent();
    }

    private IEnumerable<AuditRecord> Records(SeedRequest request)
    {
        var start = clock.GetUtcNow();
        for (var i = 0; i < request.Count; i++)
        {
            var before = i * 100L;
            var transactionId = TestLedger.AddTransaction(db, request.WalletId, 100, caller.Subject, correlation.CorrelationId, start);
            yield return AuditRecord.For(
                AuditAction.Credit,
                request.WalletId,
                transactionId,
                before,
                before + 100,
                caller.Subject,
                correlation.CorrelationId,
                request.SameInstant ? start : start.AddMilliseconds(i));
        }
    }

    public sealed record SeedRequest(Guid WalletId, int Count, bool SameInstant = false);
}
