namespace NovaWallet.Domain;

/// <summary>Shared size limits, so a column and the validation in front of it can't drift apart.</summary>
public static class Identifiers
{
    /// <summary>Customer ids, token subjects (actors, callers) and correlation ids.</summary>
    public const int MaxLength = 64;

    /// <summary>A SHA-256 digest written as hex.</summary>
    public const int Sha256HexLength = 64;
}
