using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NovaWallet.Application.Abstractions;

namespace NovaWallet.Api.Auth;

public static class AuthSetup
{
    public const string ScopeClaim = "scope";
    public const string SubjectClaim = "sub";
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromSeconds(30);

    public static IServiceCollection AddNovaWalletAuth(this IServiceCollection services)
    {
        services.AddOptions<AuthSettings>()
            .BindConfiguration(AuthSettings.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AuthSettings>, AuthSettingsValidator>();
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<AuthSettings>>().Value);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthSettings>>((options, auth) =>
            {
                var settings = auth.Value;
                options.MapInboundClaims = false;

                // Don't tell a caller which check their token failed (signature, audience, expiry).
                options.IncludeErrorDetails = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = settings.Issuer,
                    ValidAudience = settings.Audience,
                    IssuerSigningKey = SigningKey(settings),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = AllowedClockSkew,
                    NameClaimType = SubjectClaim,
                };
            });

        services.AddAuthorization(options =>
        {
            // Secure by default: an endpoint without an explicit policy still needs a valid token.
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            foreach (var scope in Scopes.All)
            {
                options.AddPolicy(scope, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireAssertion(context => HasScope(context.User, scope)));
            }
        });

        services.AddHttpContextAccessor();
        services.AddScoped<ICallerContext, HttpCallerContext>();
        services.AddSingleton<DevTokenIssuer>();
        return services;
    }

    public static SymmetricSecurityKey SigningKey(AuthSettings settings) =>
        new(Encoding.UTF8.GetBytes(settings.SigningKey ?? string.Empty));

    public static bool HasScope(ClaimsPrincipal user, string scope) =>
        user.FindAll(ScopeClaim)
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);
}
