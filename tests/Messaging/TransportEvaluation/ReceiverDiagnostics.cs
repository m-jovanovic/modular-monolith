using Microsoft.Extensions.Logging;
using Npgsql;

namespace EquipmentLibrary.TransportEvaluation;

internal sealed class ReceiverDiagnostics : ILoggerProvider, ILogger
{
    internal TaskCompletionSource Denied { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static bool IsSchemaDenial(Exception? exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.InsufficientPrivilege } error &&
                error.MessageText.Contains("schema lending", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public ILogger CreateLogger(string categoryName) => this;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (IsSchemaDenial(exception)) Denied.TrySetResult();
    }

    public void Dispose() { }
}
