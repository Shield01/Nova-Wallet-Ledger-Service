using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;

namespace NovaWallet.Api.Observability;

/// <summary>
/// Outermost middleware: accepts or generates the request's correlation id, echoes it, puts it
/// and the trace id in the logging scope, and writes one completion line per request.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemKey = "NovaWallet.CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = Accept(context.Request.Headers[HeaderName].ToString());
        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        using var scope = logger.BeginScope("CorrelationId:{CorrelationId} TraceId:{TraceId}", correlationId, traceId);

        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(unmatched)";
            LogCompleted(logger, context.Request.Method, route, context.Response.StatusCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    public static string? Current(HttpContext context) => context.Items[ItemKey] as string;

    private static string Accept(string? inbound) =>
        inbound is not null && AllowedFormat().IsMatch(inbound) ? inbound : Guid.NewGuid().ToString("N");

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex AllowedFormat();

    [LoggerMessage(Level = LogLevel.Information, Message = "{Method} {Route} responded {StatusCode} in {ElapsedMilliseconds:0.0} ms")]
    private static partial void LogCompleted(ILogger logger, string method, string route, int statusCode, double elapsedMilliseconds);
}
