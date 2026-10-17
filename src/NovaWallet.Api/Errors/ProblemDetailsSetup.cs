using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using NovaWallet.Domain.Errors;

namespace NovaWallet.Api.Errors;

public static class ProblemDetailsSetup
{
    public const string CodeExtension = "code";
    public const string TraceIdExtension = "traceId";

    public static IServiceCollection AddNovaWalletProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            Complete(context.ProblemDetails, context.HttpContext));
        services.AddExceptionHandler<UnhandledExceptionHandler>();
        services.Configure<MvcOptions>(options =>
            options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider()));
        services.Configure<ApiBehaviorOptions>(options =>
            options.InvalidModelStateResponseFactory = ValidationProblems.FromModelState);
        return services;
    }

    /// <summary>Fills in the fields every NovaWallet problem response carries.</summary>
    public static void Complete(ProblemDetails problem, HttpContext httpContext)
    {
        var status = problem.Status ?? httpContext.Response.StatusCode;
        problem.Status = status;

        if (!problem.Extensions.TryGetValue(CodeExtension, out var codeValue) || codeValue is not string code || !ErrorCatalogue.Contains(code))
        {
            code = ErrorCatalogue.CodeForStatus(status);
            problem.Extensions[CodeExtension] = code;
        }

        problem.Type = ErrorCatalogue.TypeFor(code);
        problem.Title = ErrorCatalogue.TitleFor(code);
        problem.Detail ??= ErrorCatalogue.TitleFor(code);
        problem.Instance = httpContext.Request.Path;
        problem.Extensions[TraceIdExtension] = Activity.Current?.Id ?? httpContext.TraceIdentifier;
    }

    /// <summary>Converts a business refusal into a Problem Details result.</summary>
    public static IResult ToProblem(this Error error, HttpContext httpContext)
    {
        var problem = new ProblemDetails
        {
            Status = ErrorCatalogue.StatusFor(error.Code),
            Title = ErrorCatalogue.TitleFor(error.Code),
            Detail = error.Detail,
        };
        problem.Extensions[CodeExtension] = error.Code;
        foreach (var (name, value) in error.Extensions)
        {
            problem.Extensions[name] = value;
        }

        Complete(problem, httpContext);
        return Results.Problem(problem);
    }
}
