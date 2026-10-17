using System.Security.Cryptography;
using System.Text;
using NovaWallet.Domain.Errors;

namespace NovaWallet.Application.Idempotency;

/// <summary>One idempotent call: who made it, with which key, and a fingerprint of what was asked.</summary>
public sealed record IdempotentRequest(string CallerId, string Key, string RequestHash)
{
    public const int MaxKeyLength = 128;

    /// <summary>
    /// Builds the request from the operation name and its parsed values, in a fixed order, so the
    /// fingerprint doesn't depend on JSON property order or whitespace.
    /// </summary>
    public static IdempotentRequest For(string callerId, string key, string operation, params (string Name, string Value)[] values)
    {
        var canonical = new StringBuilder(operation);
        foreach (var (name, value) in values)
        {
            canonical.Append('\n').Append(name).Append('=').Append(value);
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
        return new IdempotentRequest(callerId, key, hash);
    }

    /// <summary>Checks an Idempotency-Key header value; returns the refusal, or null when it is usable.</summary>
    public static Error? Validate(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return new Error(ErrorCodes.IdempotencyKeyRequired, "Send an Idempotency-Key header; reuse it when you retry this request.");
        }

        if (key.Length > MaxKeyLength || key.Any(c => c < '!' || c > '~'))
        {
            return new Error(ErrorCodes.IdempotencyKeyInvalid, $"An Idempotency-Key is 1-{MaxKeyLength} printable ASCII characters with no spaces.");
        }

        return null;
    }
}
