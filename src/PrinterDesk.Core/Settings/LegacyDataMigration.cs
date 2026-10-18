namespace PrinterDesk.Core.Settings;

/// <summary>
/// One-time move of user data from the app's former name (ScanDesk): settings from
/// <c>%APPDATA%\ScanDesk</c> and unsaved pages from <c>%LOCALAPPDATA%\ScanDesk\Session</c>.
/// Old logs are left behind. Browser profiles of the former WebView2 preview (tens of MB) are deleted.
/// </summary>
public static class LegacyDataMigration
{
    private const string OldName = "ScanDesk";
    private const string NewName = "PrinterDesk";

    public static void Run() => Run(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>Does nothing for data that already exists under the new name.</summary>
    public static void Run(string roamingRoot, string localRoot)
    {
        MoveIfNew(Path.Combine(roamingRoot, OldName, "settings.json"), Path.Combine(roamingRoot, NewName, "settings.json"), isFolder: false);
        MoveIfNew(Path.Combine(localRoot, OldName, "Session"), Path.Combine(localRoot, NewName, "Session"), isFolder: true);
        foreach (var name in new[] { OldName, NewName })
            DeleteFolder(Path.Combine(localRoot, name, "WebView2"));
    }

    private static void DeleteFolder(string folder)
    {
        if (!Directory.Exists(folder))
            return;
        try
        {
            Directory.Delete(folder, recursive: true);
            Log.Info($"Deleted {folder}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Could not delete {folder}", ex);
        }
    }

    private static void MoveIfNew(string source, string target, bool isFolder)
    {
        if (isFolder ? !Directory.Exists(source) || Directory.Exists(target) : !File.Exists(source) || File.Exists(target))
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (isFolder)
                Directory.Move(source, target);
            else
                File.Move(source, target);
            Log.Info($"Migrated {source} -> {target}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Migration must never block startup; the user just starts with defaults.
            Log.Error($"Could not migrate {source}", ex);
        }
    }
}
