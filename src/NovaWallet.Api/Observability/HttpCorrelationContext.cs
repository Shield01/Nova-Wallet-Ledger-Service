using NovaWallet.Application.Abstractions;

namespace NovaWallet.Api.Observability;

public sealed class HttpCorrelationContext(IHttpContextAccessor accessor) : ICorrelationContext
{
    private readonly Lazy<string> _fallback = new(() => Guid.NewGuid().ToString("N"));

    // Outside a request (background work) each scope gets its own id.
    public string CorrelationId =>
        (accessor.HttpContext is { } context ? CorrelationIdMiddleware.Current(context) : null) ?? _fallback.Value;
}
