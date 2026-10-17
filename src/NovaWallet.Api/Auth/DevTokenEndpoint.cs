using System.Text.RegularExpressions;
using NovaWallet.Api.Errors;

namespace NovaWallet.Api.Auth;

public static partial class DevTokenEndpoint
{
    public const string Route = "/dev/token";

    public static void MapDevTokenEndpoint(this IEndpointRouteBuilder endpoints, IConfiguration configuration)
    {
        // Startup validation guarantees this is only true in Development.
        if (!configuration.GetValue<bool>($"{AuthSettings.SectionName}:DevIssuer:Enabled"))
        {
            return;
        }

        endpoints.MapPost(Route, (DevTokenRequest? request, DevTokenIssuer issuer, HttpContext httpContext) =>
            {
                var errors = Validate(request);
                if (errors.Count > 0)
                {
                    return Results.Problem(ValidationProblems.Build(httpContext, errors));
                }

                // Validate guarantees a subject and a non-empty list of known scopes.
                var (token, expiresAt) = issuer.Issue(request!.Subject!, request.Scopes!.Select(s => s!).Distinct(StringComparer.Ordinal));
                return Results.Ok(new DevTokenResponse(token, "Bearer", expiresAt));
            })
            .AllowAnonymous()
            .WithName("IssueDevToken")
            .WithTags("Development")
            .Produces<DevTokenResponse>()
            .ProducesValidationProblem();
    }

    private static Dictionary<string, string[]> Validate(DevTokenRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request?.Subject is null || !SubjectPattern().IsMatch(request.Subject))
        {
            errors["subject"] = ["Must be 1-64 characters of letters, digits, '_' or '-'."];
        }

        if (request?.Scopes is null || request.Scopes.Count == 0)
        {
            errors["scopes"] = ["At least one scope is required."];
        }
        else if (request.Scopes.Any(s => s is null || !Scopes.All.Contains(s)))
        {
            // Every element must be exactly one known scope: a null or a space-joined pair would
            // otherwise slip past the checks and be split back into several scopes in the token.
            errors["scopes"] = [$"Each scope must be one of: {string.Join(", ", Scopes.All)}."];
        }
        else if (request.Scopes.Contains(Scopes.Wallet) && request.Scopes.Contains(Scopes.LedgerCredit))
        {
            // A customer must never be able to credit money into a wallet.
            errors["scopes"] = [$"'{Scopes.Wallet}' and '{Scopes.LedgerCredit}' can't be combined in one token."];
        }

        return errors;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,64}\z")]
    private static partial Regex SubjectPattern();

    public sealed record DevTokenRequest(string? Subject, IReadOnlyList<string?>? Scopes);

    public sealed record DevTokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);
}
