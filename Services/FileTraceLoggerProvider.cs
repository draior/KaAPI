using Microsoft.Extensions.Logging;
using System.Threading;

namespace KaApi.Services;

public sealed class FileTraceLoggerProvider : ILoggerProvider
{
    private readonly FileTraceSink _sink;

    public FileTraceLoggerProvider(RuntimeFilePaths runtimeFilePaths)
    {
        _sink = new FileTraceSink(runtimeFilePaths);
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileTraceLogger(categoryName, _sink);
    }

    public void Dispose()
    {
        _sink.Dispose();
    }

    private sealed class FileTraceLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly FileTraceSink _sink;

        public FileTraceLogger(string categoryName, FileTraceSink sink)
        {
            _categoryName = categoryName;
            _sink = sink;
        }

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
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
            {
                return;
            }

            _sink.Write(
                DateTime.Now,
                logLevel,
                _categoryName,
                eventId,
                message,
                exception);
        }
    }

    private sealed class FileTraceSink : IDisposable
    {
        private readonly object _sync = new();
        private readonly RuntimeFilePaths _runtimeFilePaths;
        private StreamWriter? _writer;
        private DateOnly? _currentDate;

        public FileTraceSink(RuntimeFilePaths runtimeFilePaths)
        {
            _runtimeFilePaths = runtimeFilePaths;
            _runtimeFilePaths.EnsureTraceDirectory();
            EnsureWriter(runtimeFilePaths.StartedAt);
            WriteBanner(runtimeFilePaths.StartedAt);
        }

        public void Write(
            DateTime timestamp,
            LogLevel logLevel,
            string categoryName,
            EventId eventId,
            string message,
            Exception? exception)
        {
            try
            {
                lock (_sync)
                {
                    EnsureWriter(timestamp);

                    if (_writer is null)
                    {
                        return;
                    }

                    _writer.Write(timestamp.ToString("HH:mm:ss.fff"));
                    _writer.Write(" ThreadID=");
                    _writer.Write(Environment.CurrentManagedThreadId);
                    _writer.Write(" [");
                    _writer.Write(ToShortLogLevel(logLevel));
                    _writer.Write("]  ");
                    _writer.WriteLine(message);

                    if (exception is not null)
                    {
                        _writer.WriteLine(exception);
                    }
                }
            }
            catch
            {
                // Avoid breaking the application because of trace file I/O.
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _writer?.Dispose();
            }
        }

        private void EnsureWriter(DateTime timestamp)
        {
            var logDate = DateOnly.FromDateTime(timestamp);
            if (_writer is not null && _currentDate == logDate)
            {
                return;
            }

            _writer?.Dispose();

            _writer = new StreamWriter(new FileStream(
                _runtimeFilePaths.GetTraceFilePath(timestamp),
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite))
            {
                AutoFlush = true
            };
            _currentDate = logDate;
        }

        private void WriteBanner(DateTime timestamp)
        {
            Write(
                timestamp,
                LogLevel.Information,
                "KaApi.Trace",
                new EventId(0, "Startup"),
                $"Trace file active at '{_runtimeFilePaths.GetTraceFilePath(timestamp)}'.",
                null);
        }

        private static string ToShortLogLevel(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Trace => "TRC",
                LogLevel.Debug => "DBG",
                LogLevel.Information => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                LogLevel.Critical => "CRT",
                _ => "NON"
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
