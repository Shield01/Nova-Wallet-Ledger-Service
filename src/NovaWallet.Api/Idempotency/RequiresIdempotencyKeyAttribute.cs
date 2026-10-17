namespace NovaWallet.Api.Idempotency;

/// <summary>Marks an endpoint that requires the Idempotency-Key header (used by the OpenAPI document).</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequiresIdempotencyKeyAttribute : Attribute;
