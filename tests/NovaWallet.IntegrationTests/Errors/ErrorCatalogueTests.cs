using NovaWallet.Api.Errors;
using NovaWallet.Domain.Errors;

namespace NovaWallet.IntegrationTests.Errors;

public class ErrorCatalogueTests
{
    // The catalogue in architecture.md "Errors"; a change there must be made here deliberately.
    public static readonly TheoryData<string, int> Expected = new()
    {
        { "VALIDATION_FAILED", 400 },
        { "IDEMPOTENCY_KEY_REQUIRED", 400 },
        { "IDEMPOTENCY_KEY_INVALID", 400 },
        { "UNAUTHENTICATED", 401 },
        { "FORBIDDEN", 403 },
        { "WALLET_NOT_FOUND", 404 },
        { "NOT_FOUND", 404 },
        { "METHOD_NOT_ALLOWED", 405 },
        { "UNSUPPORTED_MEDIA_TYPE", 415 },
        { "WALLET_ALREADY_EXISTS", 409 },
        { "IDEMPOTENCY_REQUEST_IN_PROGRESS", 409 },
        { "IDEMPOTENCY_KEY_REUSED", 422 },
        { "INSUFFICIENT_FUNDS", 422 },
        { "DAILY_LIMIT_EXCEEDED", 422 },
        { "SAME_WALLET_TRANSFER", 422 },
        { "DESTINATION_WALLET_NOT_FOUND", 422 },
        { "RATE_LIMITED", 429 },
        { "INTERNAL_ERROR", 500 },
        { "SERVICE_UNAVAILABLE", 503 },
    };

    [Fact]
    public void Every_domain_error_code_has_a_mapping()
    {
        Assert.All(ErrorCodes.All, code => Assert.True(ErrorCatalogue.Contains(code), $"{code} has no mapping"));
    }

    [Theory]
    [MemberData(nameof(Expected))]
    public void Each_code_maps_to_the_documented_status(string code, int status)
    {
        Assert.Equal(status, ErrorCatalogue.StatusFor(code));
    }

    [Fact]
    public void Catalogue_and_documentation_list_the_same_codes()
    {
        var documented = Expected.Select(row => (string)row[0]).Order().ToArray();

        Assert.Equal(documented, ErrorCodes.All.Order().ToArray());
    }
}
