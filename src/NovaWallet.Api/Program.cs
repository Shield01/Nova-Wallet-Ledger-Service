using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using NovaWallet.Api.Errors;
using NovaWallet.Infrastructure;
using NovaWallet.Infrastructure.Persistence;

const string MigrateArgument = "--migrate";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddNovaWalletProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddControllers().AddJsonOptions(options =>
{
    // The web defaults accept numbers written as strings; amounts must be real JSON integers.
    options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

if (args.Contains(MigrateArgument))
{
    var connectionString = app.Configuration.GetConnectionString(DatabaseSetup.MigrationsConnectionName)
        ?? throw new InvalidOperationException(
            $"Connection string '{DatabaseSetup.MigrationsConnectionName}' is not configured. " +
            $"Set ConnectionStrings__{DatabaseSetup.MigrationsConnectionName}.");
    await DatabaseMigrator.MigrateAsync(connectionString, app.Logger, CancellationToken.None);
    return;
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapControllers();

app.Run();

public partial class Program;
