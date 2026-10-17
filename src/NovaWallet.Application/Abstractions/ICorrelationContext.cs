namespace NovaWallet.Application.Abstractions;

/// <summary>The correlation id of the work in progress, carried into audit records and events.</summary>
public interface ICorrelationContext
{
    string CorrelationId { get; }
}
