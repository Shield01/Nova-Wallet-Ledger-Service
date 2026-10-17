using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace NovaWallet.IntegrationTests.Infrastructure;

/// <summary>Mints JWTs the API under test accepts (or, with overrides, deliberately rejects).</summary>
public sealed class TestTokens(string signingKey, string issuer, string audience)
{
    public const string WalletScope = "wallet";
    public const string CreditScope = "ledger:credit";
    public const string AuditScope = "audit:read";

    public string For(string subject, params string[] scopes) => Create(subject, scopes);

    public string Create(
        string subject,
        IEnumerable<string> scopes,
        string? overrideIssuer = null,
        string? overrideAudience = null,
        string? overrideSigningKey = null,
        DateTime? expires = null,
        DateTime? notBefore = null,
        string algorithm = SecurityAlgorithms.HmacSha256)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(overrideSigningKey ?? signingKey));
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = overrideIssuer ?? issuer,
            Audience = overrideAudience ?? audience,
            NotBefore = notBefore ?? now.AddMinutes(-1),
            IssuedAt = notBefore ?? now.AddMinutes(-1),
            Expires = expires ?? now.AddMinutes(15),
            Subject = new ClaimsIdentity([new Claim("sub", subject), new Claim("scope", string.Join(' ', scopes))]),
            SigningCredentials = new SigningCredentials(key, algorithm),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
