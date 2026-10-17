using Microsoft.Extensions.DependencyInjection;
using NovaWallet.Application.Wallets;

namespace NovaWallet.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<WalletService>();
        return services;
    }
}
