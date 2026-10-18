using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NovaWallet.Api.Errors;
using NovaWallet.Domain.Errors;
using NovaWallet.Infrastructure.Persistence;

namespace NovaWallet.Api.Observability;

public static class HealthSetup
{
    public const string LivePath = "/health/live";
    public const string ReadyPath = "/health/ready";
    private const string ReadyTag = "ready";

    public static IServiceCollection AddNovaWalletHealth(this IServiceCollection services)
    {
        services.AddOptions<HealthSettings>()
            .BindConfiguration(HealthSettings.SectionName)
            .Validate(s => s.ReadinessTimeoutMilliseconds >= 1 && s.ReadinessCacheMilliseconds >= 0, "Health:ReadinessTimeoutMilliseconds must be at least 1 and Health:ReadinessCacheMilliseconds not negative.")
            .ValidateOnStart();
        services.AddSingleton<ReadinessCache>();
        services.AddHealthChecks().AddCheck<DatabaseReadinessCheck>("database", HealthStatus.Unhealthy, [ReadyTag]);
        return services;
    }

    public static void MapNovaWalletHealth(this IEndpointRouteBuilder endpoints)
    {
        // Liveness runs no checks: the process answering is the signal.
        endpoints.MapHealthChecks(LivePath, new HealthCheckOptions { Predicate = _ => false, ResponseWriter = WriteAsync })
            .AllowAnonymous();
        endpoints.MapHealthChecks(ReadyPath, new HealthCheckOptions { Predicate = c => c.Tags.Contains(ReadyTag), ResponseWriter = WriteAsync })
            .AllowAnonymous();
    }

    // Healthy: a small JSON status. Otherwise: Problem Details with no detail about why, so
    // connection strings, host names and exception messages never leave the service.
    private static async Task WriteAsync(HttpContext context, HealthReport report)
    {
        if (report.Status == HealthStatus.Healthy)
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { status = "Healthy" }));
            return;
        }

        var problem = new ProblemDetails { Status = StatusCodes.Status503ServiceUnavailable, Detail = "A dependency is not ready." };
        problem.Extensions[ProblemDetailsSetup.CodeExtension] = ErrorCodes.ServiceUnavailable;
        ProblemDetailsSetup.Complete(problem, context);
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await Results.Problem(problem).ExecuteAsync(context);
    }

    private sealed class DatabaseReadinessCheck(DatabaseProbe probe, ReadinessCache cache, IOptions<HealthSettings> settings) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            cache.GetAsync(ProbeAsync, TimeSpan.FromMilliseconds(settings.Value.ReadinessCacheMilliseconds), cancellationToken);

        // The exception goes into the result so the health-check log says why; the response
        // writer never puts it in the body.
        private async Task<HealthCheckResult> ProbeAsync(CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(settings.Value.ReadinessTimeoutMilliseconds);
            try
            {
                await probe.QueryAsync(timeout.Token);
                return HealthCheckResult.Healthy();
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                return HealthCheckResult.Unhealthy("The database did not answer a query.", exception);
            }
        }
    }
}
