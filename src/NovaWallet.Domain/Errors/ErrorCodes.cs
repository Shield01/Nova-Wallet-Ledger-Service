namespace NovaWallet.Domain.Errors;

/// <summary>
/// Stable machine-readable error codes. Clients branch on these, so they never change meaning.
/// Every code here has exactly one HTTP mapping in the Api's error catalogue.
/// </summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string IdempotencyKeyRequired = "IDEMPOTENCY_KEY_REQUIRED";
    public const string IdempotencyKeyInvalid = "IDEMPOTENCY_KEY_INVALID";
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string Forbidden = "FORBIDDEN";
    public const string WalletNotFound = "WALLET_NOT_FOUND";
    public const string NotFound = "NOT_FOUND";
    public const string MethodNotAllowed = "METHOD_NOT_ALLOWED";
    public const string UnsupportedMediaType = "UNSUPPORTED_MEDIA_TYPE";
    public const string WalletAlreadyExists = "WALLET_ALREADY_EXISTS";
    public const string IdempotencyRequestInProgress = "IDEMPOTENCY_REQUEST_IN_PROGRESS";
    public const string IdempotencyKeyReused = "IDEMPOTENCY_KEY_REUSED";
    public const string InsufficientFunds = "INSUFFICIENT_FUNDS";
    public const string DailyLimitExceeded = "DAILY_LIMIT_EXCEEDED";
    public const string SameWalletTransfer = "SAME_WALLET_TRANSFER";
    public const string DestinationWalletNotFound = "DESTINATION_WALLET_NOT_FOUND";
    public const string RateLimited = "RATE_LIMITED";
    public const string InternalError = "INTERNAL_ERROR";
    public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";

    public static IReadOnlyList<string> All { get; } = typeof(ErrorCodes)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToArray();
}
