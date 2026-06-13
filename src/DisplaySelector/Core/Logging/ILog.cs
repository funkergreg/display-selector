namespace DisplaySelector.Core.Logging;

public enum LogLevel
{
    Info,
    Debug,
}

/// <summary>
/// Minimal logging abstraction. Platform-free so services can log without depending on the
/// concrete <see cref="FileLogger"/>. Debug messages are emitted only when <see cref="Level"/>
/// is <see cref="LogLevel.Debug"/> — that is where full data shapes / API traces go.
/// </summary>
public interface ILog
{
    LogLevel Level { get; set; }

    void Info(string message);

    void Debug(string message);

    void Error(string message, Exception? ex = null);
}
