namespace Luma.Host;

/// <summary>
/// The app's own log: one line per event (navigation, theme switches, presses, launches, errors),
/// flushed as it's written so it survives a crash or a forced quit. Kept small: the file is
/// rotated to app.log.1 when it passes 1 MB. Never logs file contents, keys or personal data
/// beyond paths and game names.
/// </summary>
public static class AppLog
{
    private const long MaxBytes = 1 << 20;
    private static readonly object Gate = new();
    private static StreamWriter? _writer;

    public static string? FilePath { get; private set; }

    public static void Open(string dir)
    {
        lock (Gate)
        {
            if (_writer != null)
            {
                return;
            }
            try
            {
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "app.log");
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                {
                    File.Move(path, path + ".1", overwrite: true);
                }
                _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
                FilePath = path;
                _writer.WriteLine();
                Write("INFO", $"started, version {Updates.AppUpdater.DisplayVersion(Updates.AppUpdater.CurrentAppVersion())}");
                AppDomain.CurrentDomain.UnhandledException += (_, e) => Error("unhandled: " + e.ExceptionObject);
                TaskScheduler.UnobservedTaskException += (_, e) => Error("unobserved task: " + e.Exception);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _writer = null;
            }
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        lock (Gate)
        {
            try
            {
                _writer?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {level} {message}");
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
            }
        }
    }
}
