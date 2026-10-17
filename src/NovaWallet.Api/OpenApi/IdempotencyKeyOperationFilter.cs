using Microsoft.OpenApi.Models;
using NovaWallet.Api.Idempotency;
using NovaWallet.Application.Idempotency;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NovaWallet.Api.OpenApi;

/// <summary>Documents the required Idempotency-Key header on every endpoint that carries the marker.</summary>
public sealed class IdempotencyKeyOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<RequiresIdempotencyKeyAttribute>().Any())
        {
            return;
        }

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = IdempotencyHttp.KeyHeader,
            In = ParameterLocation.Header,
            Required = true,
            Description = "A client-chosen key, unique per logical operation. A retry with the same key and body " +
                "replays the original response (`Idempotent-Replayed: true`); the same key with a different body is 422.",
            Schema = new OpenApiSchema { Type = "string", MinLength = 1, MaxLength = IdempotentRequest.MaxKeyLength },
        });
    }
}
