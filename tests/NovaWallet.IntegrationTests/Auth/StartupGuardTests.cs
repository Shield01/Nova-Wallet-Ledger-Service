using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using NovaWallet.IntegrationTests.Infrastructure;

namespace NovaWallet.IntegrationTests.Auth;

public class StartupGuardTests(NovaWalletApiFactory factory) : IClassFixture<NovaWalletApiFactory>
{
    private static readonly string ComposeDevelopmentKey = ReadComposeDevelopmentKey();

    [Fact]
    public void Dev_issuer_outside_development_stops_startup()
    {
        var error = StartupFailure("Staging", new() { ["Auth:DevIssuer:Enabled"] = "true" });

        Assert.Contains("Auth:DevIssuer:Enabled", error);
    }

    [Fact]
    public void Committed_development_key_stops_any_non_development_startup()
    {
        var error = StartupFailure("Staging", new()
        {
            ["Auth:DevIssuer:Enabled"] = "false",
            ["Auth:SigningKey"] = ComposeDevelopmentKey,
        });

        Assert.Contains("development default", error);
    }

    [Fact]
    public void Missing_signing_key_stops_startup()
    {
        var error = StartupFailure("Development", new() { ["Auth:SigningKey"] = "" });

        Assert.Contains("Auth:SigningKey", error);
    }

    [Fact]
    public void Thirty_one_byte_key_is_refused_and_thirty_two_bytes_start()
    {
        var error = StartupFailure("Development", new() { ["Auth:SigningKey"] = new string('k', 31) });
        using var ok = factory.WithSettings(new Dictionary<string, string?> { ["Auth:SigningKey"] = new string('k', 32) });

        Assert.Contains("at least 32 bytes", error);
        Assert.NotNull(ok.CreateClient());
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
            ["Auth:SigningKey"] = ComposeDevelopmentKey,
        });

        Assert.Contains("development default", error);
        Assert.DoesNotContain(ComposeDevelopmentKey, error);
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

    // Read from docker-compose.yml so the test proves the real committed default is refused.
    private static string ReadComposeDevelopmentKey()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
        {
            directory = directory.Parent;
        }

        var compose = File.ReadAllText(Path.Combine(directory!.FullName, "docker-compose.yml"));
        var match = System.Text.RegularExpressions.Regex.Match(compose, @"Auth__SigningKey: \$\{NOVAWALLET_SIGNING_KEY:-(?<key>[^}]+)\}");
        Assert.True(match.Success, "docker-compose.yml no longer has the expected Auth__SigningKey default.");
        return match.Groups["key"].Value;
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
