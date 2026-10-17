using NovaWallet.Domain.Errors;

namespace NovaWallet.Api.Errors;

/// <summary>The single mapping from error code to HTTP status and title (docs/ARCHITECTURE.md "Errors").</summary>
public static class ErrorCatalogue
{
    public const string TypeBaseUri = "https://novawallet.example/errors/";

    private static readonly Dictionary<string, (int Status, string Title)> Entries = new()
    {
        [ErrorCodes.ValidationFailed] = (StatusCodes.Status400BadRequest, "The request is invalid."),
        [ErrorCodes.IdempotencyKeyRequired] = (StatusCodes.Status400BadRequest, "An Idempotency-Key header is required."),
        [ErrorCodes.IdempotencyKeyInvalid] = (StatusCodes.Status400BadRequest, "The Idempotency-Key header is invalid."),
        [ErrorCodes.Unauthenticated] = (StatusCodes.Status401Unauthorized, "Authentication is required."),
        [ErrorCodes.Forbidden] = (StatusCodes.Status403Forbidden, "You are not allowed to do this."),
        [ErrorCodes.WalletNotFound] = (StatusCodes.Status404NotFound, "Wallet not found."),
        [ErrorCodes.NotFound] = (StatusCodes.Status404NotFound, "Resource not found."),
        [ErrorCodes.MethodNotAllowed] = (StatusCodes.Status405MethodNotAllowed, "Method not allowed."),
        [ErrorCodes.UnsupportedMediaType] = (StatusCodes.Status415UnsupportedMediaType, "Unsupported media type."),
        [ErrorCodes.WalletAlreadyExists] = (StatusCodes.Status409Conflict, "The customer already has a wallet."),
        [ErrorCodes.IdempotencyRequestInProgress] = (StatusCodes.Status409Conflict, "A request with this Idempotency-Key is still being processed."),
        [ErrorCodes.IdempotencyKeyReused] = (StatusCodes.Status422UnprocessableEntity, "This Idempotency-Key was used for a different request."),
        [ErrorCodes.InsufficientFunds] = (StatusCodes.Status422UnprocessableEntity, "Insufficient funds."),
        [ErrorCodes.DailyLimitExceeded] = (StatusCodes.Status422UnprocessableEntity, "Daily transfer limit exceeded."),
        [ErrorCodes.SameWalletTransfer] = (StatusCodes.Status422UnprocessableEntity, "Source and destination wallets are the same."),
        [ErrorCodes.DestinationWalletNotFound] = (StatusCodes.Status422UnprocessableEntity, "Destination wallet not found."),
        [ErrorCodes.RateLimited] = (StatusCodes.Status429TooManyRequests, "Too many requests."),
        [ErrorCodes.InternalError] = (StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
        [ErrorCodes.ServiceUnavailable] = (StatusCodes.Status503ServiceUnavailable, "The service is temporarily unavailable."),
    };

    private static readonly Dictionary<int, string> CodeForBareStatus = new()
    {
        [StatusCodes.Status400BadRequest] = ErrorCodes.ValidationFailed,
        [StatusCodes.Status401Unauthorized] = ErrorCodes.Unauthenticated,
        [StatusCodes.Status403Forbidden] = ErrorCodes.Forbidden,
        [StatusCodes.Status404NotFound] = ErrorCodes.NotFound,
        [StatusCodes.Status405MethodNotAllowed] = ErrorCodes.MethodNotAllowed,
        [StatusCodes.Status415UnsupportedMediaType] = ErrorCodes.UnsupportedMediaType,
        [StatusCodes.Status429TooManyRequests] = ErrorCodes.RateLimited,
        [StatusCodes.Status500InternalServerError] = ErrorCodes.InternalError,
        [StatusCodes.Status503ServiceUnavailable] = ErrorCodes.ServiceUnavailable,
    };

    public static bool Contains(string code) => Entries.ContainsKey(code);

    public static int StatusFor(string code) => Entries[code].Status;

    public static string TitleFor(string code) => Entries[code].Title;

    public static string TypeFor(string code) => TypeBaseUri + code.ToLowerInvariant().Replace('_', '-');

    /// <summary>The code used when the framework produces an error with no code of its own.</summary>
    public static string CodeForStatus(int status) =>
        CodeForBareStatus.TryGetValue(status, out var code) ? code : ErrorCodes.InternalError;
}
