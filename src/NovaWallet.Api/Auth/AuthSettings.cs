namespace NovaWallet.Api.Auth;

public sealed class AuthSettings
{
    public const string SectionName = "Auth";
    public const int MinimumSigningKeyBytes = 32;

    /// <summary>
    /// SHA-256 (hex) of the development-only key committed in docker-compose.yml. Production refuses
    /// a key with this fingerprint; the key itself is deliberately not repeated in code.
    /// </summary>
    public const string ComposeDevelopmentSigningKeySha256 = "95839f080158597d68e37eab28f43a5b0d0b99a4ecbc70a8b19eb6df678bf53d";

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
