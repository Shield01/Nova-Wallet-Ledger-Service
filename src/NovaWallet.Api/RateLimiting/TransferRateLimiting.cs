using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using NovaWallet.Api.Auth;
using NovaWallet.Api.Errors;
using NovaWallet.Domain.Errors;

namespace NovaWallet.Api.RateLimiting;

/// <summary>
/// A fixed window of transfer requests per token subject. It runs after authorisation, so a
/// request that fails authentication never takes a permit, and before the controller, so a limited
/// request posts nothing and stores no idempotency record.
/// </summary>
public static class TransferRateLimiting
{
    public const string PolicyName = "transfers-per-customer";

    public static IServiceCollection AddTransferRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<TransferRateLimitSettings>()
            .BindConfiguration(TransferRateLimitSettings.SectionName)
            .Validate(s => s.PermitLimit >= 1 && s.WindowSeconds >= 1, "RateLimiting:Transfers:PermitLimit and WindowSeconds must be at least 1.")
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(PolicyName, httpContext =>
            {
                // The scope policies require `sub`, so it is always present here. If that ever
                // changed, subject-less callers would share one bucket: limited, never unlimited.
                var subject = httpContext.User.FindFirst(AuthSetup.SubjectClaim)?.Value ?? string.Empty;
                var settings = httpContext.RequestServices.GetRequiredService<IOptions<TransferRateLimitSettings>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(subject, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.PermitLimit,
                    Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
            });
            options.OnRejected = WriteRejectionAsync;
        });
        return services;
    }

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            http.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        var problem = new ProblemDetails { Status = StatusCodes.Status429TooManyRequests, Detail = "Too many transfer requests; retry after the Retry-After interval." };
        problem.Extensions[ProblemDetailsSetup.CodeExtension] = ErrorCodes.RateLimited;
        ProblemDetailsSetup.Complete(problem, http);
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await Results.Problem(problem).ExecuteAsync(http);
    }
}
