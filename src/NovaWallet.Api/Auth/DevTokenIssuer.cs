using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace NovaWallet.Api.Auth;

/// <summary>
/// Mints tokens for local development and the assessment panel. Only reachable when
/// Auth:DevIssuer:Enabled is true, which startup refuses in Production.
/// </summary>
public sealed class DevTokenIssuer(AuthSettings settings)
{
    // The JWT library validates lifetimes against the wall clock, so tokens are issued against the
    // wall clock too, not the injectable TimeProvider that drives business time.
    private static readonly TimeProvider WallClock = TimeProvider.System;

    public (string Token, DateTimeOffset ExpiresAt) Issue(string subject, IEnumerable<string> scopes)
    {
        var now = WallClock.GetUtcNow();
        var expiresAt = now.AddMinutes(settings.TokenLifetimeMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(AuthSetup.SubjectClaim, subject),
                new Claim(AuthSetup.ScopeClaim, string.Join(' ', scopes)),
            ]),
            SigningCredentials = new SigningCredentials(AuthSetup.SigningKey(settings), SecurityAlgorithms.HmacSha256),
        };
        return (new JsonWebTokenHandler().CreateToken(descriptor), expiresAt);
    }
}
