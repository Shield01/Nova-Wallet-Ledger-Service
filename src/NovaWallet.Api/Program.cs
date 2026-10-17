using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using NovaWallet.Infrastructure;
using NovaWallet.Infrastructure.Persistence;

const string MigrateArgument = "--migrate";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks();

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

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

app.Run();

public partial class Program;
