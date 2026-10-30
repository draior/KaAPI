using Microsoft.Extensions.Logging;

namespace KaApi.Services;

public sealed class ConsoleMasterDataLoggerProvider : ILoggerProvider
{
    private const int SaleHeaderEventId = 1000;

    public ILogger CreateLogger(string categoryName)
    {
        return new ConsoleMasterDataLogger();
    }

    public void Dispose()
    {
    }

    private sealed class ConsoleMasterDataLogger : ILogger
    {
        private static readonly object Sync = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var isSaleHeader = logLevel == LogLevel.Information && eventId.Id == SaleHeaderEventId;
            if (!isSaleHeader && logLevel < LogLevel.Error)
            {
                return;
            }

            var message = formatter(state, exception);
            lock (Sync)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{ToShortLogLevel(logLevel)}] {message}");
                if (exception is not null)
                {
                    Console.Error.WriteLine(exception);
                }
            }
        }

        private static string ToShortLogLevel(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Information => "INF",
                LogLevel.Error => "ERR",
                LogLevel.Critical => "CRT",
                _ => "ERR"
            };
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
