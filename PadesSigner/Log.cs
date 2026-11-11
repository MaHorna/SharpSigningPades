namespace PadesSigner;

/// <summary>Minimal thread-safe daily file logger in %LOCALAPPDATA%\PadesSigner\logs.</summary>
public static class Log
{
    private static readonly object Sync = new();

    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PadesSigner", "logs");

    public static string CurrentFile => Path.Combine(Directory, $"PadesSigner-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}";
        lock (Sync)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(CurrentFile, line);
            }
            catch
            {
                // Logging must never take the service down.
            }
        }
    }
}
