using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NovaWallet.Api.Errors;
using NovaWallet.Api.Idempotency;
using NovaWallet.Application.Abstractions;
using NovaWallet.Application.Audit;
using NovaWallet.Application.Idempotency;
using NovaWallet.Domain.Audit;
using NovaWallet.Domain.Errors;
using NovaWallet.Infrastructure.Persistence;

namespace NovaWallet.IntegrationTests.Infrastructure;

/// <summary>
/// Drives the idempotency component through a real request and unit of work. The "operation"
/// writes an audit row (a real write in the same transaction) and counts its invocations per key.
/// </summary>
[ApiController]
[Route("test/idempotency")]
public sealed class TestIdempotencyController(
    IUnitOfWork unitOfWork,
    IdempotencyGuard guard,
    IAuditTrail audit,
    LedgerDbContext db,
    ICallerContext caller,
    ICorrelationContext correlation,
    TimeProvider clock) : ControllerBase
{
    public static readonly ConcurrentDictionary<string, int> Invocations = new();

    [HttpPost]
    [RequiresIdempotencyKey]
    public async Task<IResult> Run(Body body, CancellationToken cancellationToken)
    {
        if (!IdempotencyHttp.TryReadKey(Request, out var key, out var invalid))
        {
            return invalid!.ToProblem(HttpContext);
        }

        var request = IdempotentRequest.For(
            caller.Subject,
            key,
            "test-operation",
            ("walletId", body.WalletId.ToString()),
            ("amountKobo", body.AmountKobo.ToString(CultureInfo.InvariantCulture)));

        var outcome = await unitOfWork.InTransactionAsync(ct => guard.RunAsync(request, async inner =>
        {
            Invocations.AddOrUpdate($"{caller.Subject}:{key}", 1, (_, n) => n + 1);
            if (body.DelayMs > 0)
            {
                await Task.Delay(body.DelayMs, inner);
            }

            if (body.Refuse && !body.WriteBeforeRefusing)
            {
                return new Error(body.RefuseCode ?? ErrorCodes.InsufficientFunds, "Refused by the test operation.").With("remainingKobo", 5L);
            }

            var transactionId = TestLedger.AddTransaction(db, body.WalletId, body.AmountKobo, caller.Subject, correlation.CorrelationId, clock.GetUtcNow());
            audit.Append(AuditRecord.For(AuditAction.Credit, body.WalletId, transactionId, 0, body.AmountKobo, caller.Subject, correlation.CorrelationId, clock.GetUtcNow()));
            await db.SaveChangesAsync(inner);
            if (body.Refuse)
            {
                return new Error(body.RefuseCode ?? ErrorCodes.InsufficientFunds, "Refused after writing.").With("remainingKobo", 5L);
            }

            if (body.FailAfterWrite)
            {
                throw new InvalidOperationException("Simulated failure after the operation wrote.");
            }

            return Result<Outcome>.Success(new Outcome(Guid.NewGuid(), body.AmountKobo));
        }, ct), cancellationToken);

        return IdempotencyHttp.Render(outcome, HttpContext, value => Results.Created($"/test/idempotency/{value.Id}", value));
    }

    public sealed record Body(
        Guid WalletId,
        long AmountKobo,
        bool Refuse = false,
        bool FailAfterWrite = false,
        int DelayMs = 0,
        bool WriteBeforeRefusing = false,
        string? RefuseCode = null);

    public sealed record Outcome(Guid Id, long AmountKobo);
}
