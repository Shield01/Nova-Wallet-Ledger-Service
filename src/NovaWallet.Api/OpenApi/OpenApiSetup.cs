using System.Reflection;
using Microsoft.OpenApi.Models;

namespace NovaWallet.Api.OpenApi;

public static class OpenApiSetup
{
    public const string EnabledKey = "OpenApi:Enabled";
    public const string DocumentName = "v1";
    public const string BearerSchemeId = "Bearer";
    public const string ProblemJson = "application/problem+json";

    public static IServiceCollection AddNovaWalletOpenApi(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc(DocumentName, new OpenApiInfo
            {
                Title = "NovaWallet Ledger Service",
                Version = DocumentName,
                Description = "NGN wallets with a double-entry ledger. Amounts are integers in kobo (₦1 = 100 kobo). " +
                    "Errors are RFC 7807 Problem Details. In Development, get a token from POST /dev/token, then use Authorize.",
            });
            options.AddSecurityDefinition(BearerSchemeId, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "A JWT signed with HS256. Scopes are in the space-delimited `scope` claim.",
            });
            options.OperationFilter<SecurityOperationFilter>();
            options.OperationFilter<IdempotencyKeyOperationFilter>();
            options.SupportNonNullableReferenceTypes();

            var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
            if (File.Exists(xml))
            {
                options.IncludeXmlComments(xml);
            }
        });
        return services;
    }

    /// <summary>
    /// Serves the document and the UI before authentication runs, so both stay anonymous. When
    /// disabled nothing is mapped, and the paths behave like any unknown route.
    /// </summary>
    public static IApplicationBuilder UseNovaWalletOpenApi(this WebApplication app)
    {
        if (app.Configuration.GetValue<bool>(EnabledKey))
        {
            app.UseSwagger();
            // No persisted authorisation: a pasted token stays in page memory, never in localStorage.
            app.UseSwaggerUI(options =>
                options.SwaggerEndpoint($"/swagger/{DocumentName}/swagger.json", "NovaWallet Ledger Service v1"));
        }

        return app;
    }
}
