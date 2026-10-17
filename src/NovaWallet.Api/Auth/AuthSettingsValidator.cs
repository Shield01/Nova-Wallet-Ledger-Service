using System.Text;
using Microsoft.Extensions.Options;

namespace NovaWallet.Api.Auth;

/// <summary>Startup guards for authentication settings. Messages never include the key itself.</summary>
public sealed class AuthSettingsValidator(IHostEnvironment environment) : IValidateOptions<AuthSettings>
{
    public ValidateOptionsResult Validate(string? name, AuthSettings settings)
    {
        var failures = new List<string>();

        if (string.IsNullOrEmpty(settings.SigningKey) || Encoding.UTF8.GetByteCount(settings.SigningKey) < AuthSettings.MinimumSigningKeyBytes)
        {
            failures.Add($"Auth:SigningKey must be set (environment variable Auth__SigningKey) and be at least {AuthSettings.MinimumSigningKeyBytes} bytes.");
        }

        if (settings.DevIssuer.Enabled && !environment.IsDevelopment())
        {
            failures.Add("Auth:DevIssuer:Enabled may only be true in the Development environment; the development token issuer mints tokens for anyone.");
        }

        if (environment.IsProduction() && string.Equals(settings.SigningKey, AuthSettings.ComposeDevelopmentSigningKey, StringComparison.Ordinal))
        {
            failures.Add("Auth:SigningKey is the committed development default; set a real key through Auth__SigningKey in Production.");
        }

        if (settings.TokenLifetimeMinutes <= 0)
        {
            failures.Add("Auth:TokenLifetimeMinutes must be greater than zero.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
