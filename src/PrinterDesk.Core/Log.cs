namespace PrinterDesk.Core;

/// <summary>Minimal daily-rolling text log in <c>%LOCALAPPDATA%\PrinterDesk\logs</c> (7 days kept).</summary>
public static class Log
{
    private const int KeepDays = 7;
    private static readonly Lock Gate = new();

    public static string Folder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrinterDesk", "logs");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    /// <summary>Deletes log files older than <see cref="KeepDays"/> days.</summary>
    public static void Cleanup()
    {
        try
        {
            foreach (var file in new DirectoryInfo(Folder).EnumerateFiles("printerdesk-*.log"))
            {
                if (file.LastWriteTime < DateTime.Now.AddDays(-KeepDays))
                    file.Delete();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never break the app.
        }
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(
                    Path.Combine(Folder, $"printerdesk-{DateTime.Now:yyyyMMdd}.log"),
                    $"{DateTime.Now:HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never break the app.
        }
    }
}
