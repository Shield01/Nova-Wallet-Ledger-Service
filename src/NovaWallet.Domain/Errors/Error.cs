namespace NovaWallet.Domain.Errors;

/// <summary>A business refusal: an expected outcome, not an exception.</summary>
public sealed record Error(string Code, string Detail)
{
    public IReadOnlyDictionary<string, object?> Extensions { get; init; } = new Dictionary<string, object?>();

    public Error With(string name, object? value) =>
        this with { Extensions = new Dictionary<string, object?>(Extensions) { [name] = value } };
}
