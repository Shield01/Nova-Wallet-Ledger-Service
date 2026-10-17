namespace NovaWallet.Api.Auth;

public sealed class AuthSettings
{
    public const string SectionName = "Auth";
    public const int MinimumSigningKeyBytes = 32;

    /// <summary>The development-only key committed in docker-compose.yml; Production refuses it.</summary>
    public const string ComposeDevelopmentSigningKey = "dev-only-signing-key-do-not-use-in-production-0123456789";

    public string Issuer { get; init; } = "novawallet-dev-issuer";

    public string Audience { get; init; } = "novawallet-api";

    public string? SigningKey { get; init; }

    public int TokenLifetimeMinutes { get; init; } = 15;

    public DevIssuerSettings DevIssuer { get; init; } = new();

    public sealed class DevIssuerSettings
    {
        public bool Enabled { get; init; }
    }
}
