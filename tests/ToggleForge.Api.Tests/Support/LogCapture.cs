using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ToggleForge.Api.Tests.Support;

/// <summary>Guarda tudo o que a API registra, para provar o que nunca pode aparecer em log (STRIDE I1).</summary>
public sealed class LogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyCollection<string> Entries => _entries;

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            entries.Enqueue($"{logLevel} {category}: {formatter(state, exception)} {state} {exception}");
        }
    }
}
