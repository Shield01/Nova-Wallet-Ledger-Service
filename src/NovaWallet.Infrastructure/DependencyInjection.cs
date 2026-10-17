using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddScoped<IWalletStore, WalletStore>();
        return services;
    }
}
