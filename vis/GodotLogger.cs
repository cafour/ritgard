using System;
using Godot;
using Microsoft.Extensions.Logging;

namespace Ritgard;

public class GodotLogger : ILogger
{
    public LogLevel LogLevel { get; set; }

    public string? CategoryName { get; set; }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        var message = formatter(state, exception);
        if (CategoryName is not null)
        {
            message = $"{CategoryName}: {message}";
        }

        switch (logLevel)
        {
            case LogLevel.Critical:
            case LogLevel.Error:
                GD.PushError(message);
                break;
            case LogLevel.Warning:
                GD.PushWarning(message);
                break;
            default:
                GD.Print(message);
                break;
        }
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return (int)LogLevel <= (int)logLevel;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        throw new NotImplementedException();
    }
}
