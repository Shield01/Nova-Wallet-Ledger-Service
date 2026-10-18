using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using NovaWallet.Api.Auth;
using NovaWallet.Api.Errors;
using NovaWallet.Api.Observability;
using NovaWallet.Api.OpenApi;
using NovaWallet.Application;
using NovaWallet.Infrastructure;
using NovaWallet.Infrastructure.Persistence;

const string MigrateArgument = "--migrate";

var builder = WebApplication.CreateBuilder(args);

if (args.Contains(MigrateArgument))
{
    await MigrateAndExitAsync(builder);
    return;
}

builder.AddNovaWalletLogging();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddNovaWalletProblemDetails();
builder.Services.AddNovaWalletAuth();
builder.Services.AddHealthChecks();
builder.Services.AddNovaWalletOpenApi();
builder.Services.AddControllers().AddJsonOptions(options =>
{
    // The web defaults accept numbers written as strings; amounts must be real JSON integers.
    options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseNovaWalletOpenApi();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapDevTokenEndpoint(app.Configuration);
app.MapControllers();

app.Run();

static async Task MigrateAndExitAsync(WebApplicationBuilder builder)
{
    using var app = builder.Build();
    var connectionString = app.Configuration.GetConnectionString(DatabaseSetup.MigrationsConnectionName)
        ?? throw new InvalidOperationException(
            $"Connection string '{DatabaseSetup.MigrationsConnectionName}' is not configured. " +
            $"Set ConnectionStrings__{DatabaseSetup.MigrationsConnectionName}.");
    await DatabaseMigrator.MigrateAsync(connectionString, app.Logger, CancellationToken.None);
}

public partial class Program;
