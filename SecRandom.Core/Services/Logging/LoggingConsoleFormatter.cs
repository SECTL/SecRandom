using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Pastel;

namespace SecRandom.Core.Services.Logging;

public class LoggingConsoleFormatter() : ConsoleFormatter("secrandom")
{
    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var separator = " | ".Pastel(ConsoleColor.Gray);
        var scopeSeparator = " => ".Pastel(ConsoleColor.Gray);
        var now = DateTimeOffset.Now.ToString("yyyy/MM/dd HH:mm:ss").Pastel(ConsoleColor.DarkGray);
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception) + (logEntry.Exception != null
            ? Environment.NewLine + logEntry.Exception.ToString().Pastel("#cccccc")
            : "");

        textWriter.Write(now);
        textWriter.Write(separator);
        textWriter.Write(GetLogLevelString(logEntry.LogLevel));
        textWriter.Write(separator);
        textWriter.Write(logEntry.Category.Pastel("#acefef"));
        textWriter.Write(separator);
        scopeProvider?.ForEachScope((scope, state) =>
        {
            state.Write(scope?.ToString().Pastel("#e29cd7"));
            state.Write(scopeSeparator);
        }, textWriter);
        textWriter.Write(message);
        textWriter.Write(Environment.NewLine);
    }

    private static string GetLogLevelString(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => "trce".Pastel(ConsoleColor.Gray),
            LogLevel.Debug => "dbug".Pastel(ConsoleColor.DarkGray),
            LogLevel.Information => "info".Pastel(ConsoleColor.Green),
            LogLevel.Warning => "warn".Pastel(ConsoleColor.Yellow),
            LogLevel.Error => "fail".Pastel(ConsoleColor.White).PastelBg(ConsoleColor.DarkRed),
            LogLevel.Critical => "crit".Pastel(ConsoleColor.Black).PastelBg(ConsoleColor.Red),
            _ => throw new ArgumentOutOfRangeException(nameof(logLevel))
        };
    }
}