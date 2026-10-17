using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NovaWallet.Api.OpenApi;

/// <summary>
/// Marks every operation that needs a caller with the bearer scheme and a 401 response; anonymous
/// operations get neither. Error responses are always Problem Details.
/// </summary>
public sealed class SecurityOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any())
        {
            return;
        }

        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = OpenApiSetup.BearerSchemeId } }] = [],
        });

        if (!operation.Responses.ContainsKey("401"))
        {
            var schema = context.SchemaGenerator.GenerateSchema(typeof(ProblemDetails), context.SchemaRepository);
            operation.Responses["401"] = new OpenApiResponse
            {
                Description = "Missing, malformed, expired or wrongly signed token.",
                Content = { [OpenApiSetup.ProblemJson] = new OpenApiMediaType { Schema = schema } },
            };
        }
    }
}
