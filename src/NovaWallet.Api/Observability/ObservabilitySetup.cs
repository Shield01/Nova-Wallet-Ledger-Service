using NovaWallet.Application.Abstractions;

namespace NovaWallet.Api.Observability;

public static class ObservabilitySetup
{
    public static WebApplicationBuilder AddNovaWalletLogging(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        });
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICorrelationContext, HttpCorrelationContext>();
        return builder;
    }
}
