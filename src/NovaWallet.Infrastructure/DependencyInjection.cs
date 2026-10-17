using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NovaWallet.Application.Abstractions;
using NovaWallet.Application.Audit;
using NovaWallet.Application.Idempotency;
using NovaWallet.Application.Ledger;
using NovaWallet.Application.Wallets;
using NovaWallet.Infrastructure.Persistence;
using NovaWallet.Infrastructure.Persistence.Stores;

namespace NovaWallet.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<LedgerDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString(DatabaseSetup.RuntimeConnectionName)
                ?? throw new InvalidOperationException(
                    $"Connection string '{DatabaseSetup.RuntimeConnectionName}' is not configured. " +
                    $"Set ConnectionStrings__{DatabaseSetup.RuntimeConnectionName}.");
            DatabaseSetup.Configure(options, connectionString);
        });
        services.AddOptions<DatabaseSettings>().BindConfiguration(DatabaseSettings.SectionName);
        services.AddOptions<LimitSettings>().BindConfiguration(LimitSettings.SectionName);
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IWalletStore, WalletStore>();
        services.AddScoped<IAuditTrail, AuditTrail>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<ILedgerStore, LedgerStore>();
        return services;
    }
}
