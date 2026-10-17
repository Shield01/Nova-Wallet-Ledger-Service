using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace NovaWallet.IntegrationTests.Infrastructure;

/// <summary>Captures every log entry with its scope values, so tests can assert on what was logged.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public ConcurrentQueue<CapturedEntry> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    public sealed record CapturedEntry(string Category, LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Scope, string? Exception);

    private sealed class CapturingLogger(CapturingLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => provider._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var scope = new Dictionary<string, object?>();
            provider._scopes.ForEachScope((value, bag) =>
            {
                if (value is IEnumerable<KeyValuePair<string, object>> pairs)
                {
                    foreach (var (key, item) in pairs)
                    {
                        bag[key] = item;
                    }
                }
            }, scope);
            provider.Entries.Enqueue(new CapturedEntry(category, logLevel, formatter(state, exception), scope, exception?.ToString()));
        }
    }
}
