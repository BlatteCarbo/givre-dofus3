using System;

namespace GivreClone.App.Services;

public enum LogLevel { Info, Warn, Error }

public readonly record struct LogEntry(DateTime Time, LogLevel Level, string Source, string Message);

/// <summary>
/// Bus de logs applicatif (diagnostic proxy) : les proxys/VMs émettent, la page Logs écoute.
/// Statique et thread-safe côté émission (les proxys émettent depuis leurs threads d'I/O) ;
/// la page marshale vers l'UI.
/// </summary>
public static class AdapterLog
{
    public static event Action<LogEntry>? Emitted;

    public static void Info(string source, string message) => Emit(LogLevel.Info, source, message);
    public static void Warn(string source, string message) => Emit(LogLevel.Warn, source, message);
    public static void Error(string source, string message) => Emit(LogLevel.Error, source, message);

    private static void Emit(LogLevel level, string source, string message)
        => Emitted?.Invoke(new LogEntry(DateTime.Now, level, source, message));
}
