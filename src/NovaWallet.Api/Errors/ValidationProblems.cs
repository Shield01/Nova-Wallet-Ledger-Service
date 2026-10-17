using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NovaWallet.Domain.Errors;

namespace NovaWallet.Api.Errors;

public static class ValidationProblems
{
    private const string BodyErrorMessage = "The request body is missing or is not valid JSON for this endpoint.";

    public static IActionResult FromModelState(ActionContext context)
    {
        var errors = Normalise(context.ModelState);
        return Create(context.HttpContext, errors);
    }

    public static ValidationProblemDetails Build(HttpContext httpContext, IDictionary<string, string[]> errors)
    {
        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = ErrorCatalogue.TitleFor(ErrorCodes.ValidationFailed),
            Detail = "One or more fields are invalid.",
        };
        problem.Extensions[ProblemDetailsSetup.CodeExtension] = ErrorCodes.ValidationFailed;
        ProblemDetailsSetup.Complete(problem, httpContext);
        return problem;
    }

    public static IActionResult Create(HttpContext httpContext, IDictionary<string, string[]> errors)
    {
        var problem = Build(httpContext, errors);
        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" },
        };
    }

    // Model binding reports JSON errors under "$.field" and a generic error for the action
    // parameter; clients only need the camelCase field names.
    private static Dictionary<string, string[]> Normalise(ModelStateDictionary modelState)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (key, entry) in modelState)
        {
            if (entry.Errors.Count == 0)
            {
                continue;
            }

            var field = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key;
            if (field.Length == 0 || field == "$")
            {
                field = "body";
            }

            if (!errors.TryGetValue(field, out var messages))
            {
                messages = [];
                errors[field] = messages;
            }

            messages.AddRange(entry.Errors.Select(e => key.StartsWith('$') || string.IsNullOrEmpty(e.ErrorMessage)
                ? BodyErrorMessage
                : e.ErrorMessage));
        }

        // When a specific field failed, drop the generic "request" entry the binder adds for the
        // whole parameter; it adds no information.
        if (errors.Count > 1)
        {
            foreach (var generic in errors.Keys.Where(k => k is "request" or "body").ToList())
            {
                errors.Remove(generic);
            }
        }

        return errors.ToDictionary(e => e.Key, e => e.Value.Distinct().ToArray(), StringComparer.Ordinal);
    }
}
