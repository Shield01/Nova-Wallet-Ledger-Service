using Microsoft.Extensions.DependencyInjection;
using NovaWallet.Application.Audit;
using NovaWallet.Application.Idempotency;
using NovaWallet.Application.Ledger;
using NovaWallet.Application.Wallets;

namespace NovaWallet.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<WalletService>();
        services.AddScoped<AuditService>();
        services.AddScoped<IdempotencyGuard>();
        services.AddScoped<CreditService>();
        services.AddScoped<TransferService>();
        services.AddScoped<StatementService>();
        return services;
    }
}
