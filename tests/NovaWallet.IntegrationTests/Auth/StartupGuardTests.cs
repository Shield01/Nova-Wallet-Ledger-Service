using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using NovaWallet.Api.Auth;
using NovaWallet.IntegrationTests.Infrastructure;

namespace NovaWallet.IntegrationTests.Auth;

public class StartupGuardTests(NovaWalletApiFactory factory) : IClassFixture<NovaWalletApiFactory>
{
    [Fact]
    public void Dev_issuer_outside_development_stops_startup()
    {
        var error = StartupFailure("Staging", new() { ["Auth:DevIssuer:Enabled"] = "true" });

        Assert.Contains("Auth:DevIssuer:Enabled", error);
    }

    [Fact]
    public void Short_signing_key_stops_startup_without_printing_it()
    {
        const string shortKey = "too-short-key";

        var error = StartupFailure("Development", new() { ["Auth:SigningKey"] = shortKey });

        Assert.Contains("Auth:SigningKey", error);
        Assert.DoesNotContain(shortKey, error);
    }

    [Fact]
    public void Committed_development_key_stops_production_startup()
    {
        var error = StartupFailure("Production", new()
        {
            ["Auth:DevIssuer:Enabled"] = "false",
            ["Auth:SigningKey"] = AuthSettings.ComposeDevelopmentSigningKey,
        });

        Assert.Contains("development default", error);
        Assert.DoesNotContain(AuthSettings.ComposeDevelopmentSigningKey, error);
    }

    private string StartupFailure(string environment, Dictionary<string, string?> settings)
    {
        using var configured = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
        });

        var exception = Assert.ThrowsAny<Exception>(() => configured.CreateClient());
        return Flatten(exception);
    }

    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }
}
