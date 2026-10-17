namespace NovaWallet.Api.Auth;

/// <summary>OAuth-style scopes carried in the space-delimited <c>scope</c> claim. Each is also a policy name.</summary>
public static class Scopes
{
    public const string Wallet = "wallet";
    public const string LedgerCredit = "ledger:credit";
    public const string AuditRead = "audit:read";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal) { Wallet, LedgerCredit, AuditRead };
}
