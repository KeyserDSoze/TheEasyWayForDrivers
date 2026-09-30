using System.Text;
using Microsoft.Extensions.Logging;

namespace TheEasyWayForDrivers.ServiceApp.Infrastructure;

public sealed class DailyFileLoggerProvider : ILoggerProvider
{
    private readonly object _sync = new();
    private readonly string _directory;
    private readonly bool _enabled;

    public DailyFileLoggerProvider(string directory)
    {
        _directory = directory;

        try
        {
            Directory.CreateDirectory(_directory);
            DeleteExpiredLogs();
            _enabled = true;
        }
        catch (IOException)
        {
            _enabled = false;
        }
        catch (UnauthorizedAccessException)
        {
            _enabled = false;
        }
    }

    public ILogger CreateLogger(string categoryName) =>
        new DailyFileLogger(this, categoryName);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    private void Write(
        LogLevel level,
        string category,
        EventId eventId,
        string message,
        Exception? exception)
    {
        if (!_enabled)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var path = Path.Combine(_directory, $"service-{now:yyyyMMdd}.log");
        var builder = new StringBuilder();

        builder.Append(now.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        builder.Append(" [");
        builder.Append(level);
        builder.Append("] ");
        builder.Append(category);

        if (eventId.Id != 0)
        {
            builder.Append(" #");
            builder.Append(eventId.Id);
        }

        builder.Append(": ");
        builder.Append(message);

        if (exception is not null)
        {
            builder.AppendLine();
            builder.Append(exception);
        }

        lock (_sync)
        {
            try
            {
                File.AppendAllText(path, builder + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void DeleteExpiredLogs()
    {
        var cutoff = DateTime.UtcNow.AddDays(-14);

        foreach (var path in Directory.EnumerateFiles(_directory, "service-*.log"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff)
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class DailyFileLogger(
        DailyFileLoggerProvider provider,
        string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel >= LogLevel.Information;

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

            provider.Write(
                logLevel,
                category,
                eventId,
                formatter(state, exception),
                exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
