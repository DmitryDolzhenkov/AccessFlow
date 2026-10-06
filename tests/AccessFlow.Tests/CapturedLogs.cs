using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace AccessFlow.Tests;

public sealed record CapturedLog(string Category, LogLevel Level, IReadOnlyDictionary<string, object?> Properties);

/// <summary>
/// Collects log entries with their structured properties, so tests can check technical logging.
/// </summary>
public sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();

    public IReadOnlyList<CapturedLog> Entries => [.. _entries];

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = state as IEnumerable<KeyValuePair<string, object?>> ?? [];
            entries.Enqueue(new CapturedLog(category, logLevel, properties.ToDictionary()));
        }
    }
}
