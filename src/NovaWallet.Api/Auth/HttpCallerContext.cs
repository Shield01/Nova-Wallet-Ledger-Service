using NovaWallet.Application.Abstractions;

namespace NovaWallet.Api.Auth;

public sealed class HttpCallerContext(IHttpContextAccessor accessor) : ICallerContext
{
    public string Subject =>
        accessor.HttpContext?.User.FindFirst(AuthSetup.SubjectClaim)?.Value
        ?? throw new InvalidOperationException("No authenticated caller for this request.");
}
