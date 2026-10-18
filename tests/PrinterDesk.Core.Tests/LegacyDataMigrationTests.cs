using PrinterDesk.Core.Settings;

namespace PrinterDesk.Core.Tests;

public class LegacyDataMigrationTests
{
    [Fact]
    public void Moves_settings_and_session_from_the_old_name()
    {
        using var temp = new TempFolder();
        var roaming = Path.Combine(temp.Path, "Roaming");
        var local = Path.Combine(temp.Path, "Local");
        Directory.CreateDirectory(Path.Combine(roaming, "ScanDesk"));
        File.WriteAllText(Path.Combine(roaming, "ScanDesk", "settings.json"), "{\"old\":1}");
        Directory.CreateDirectory(Path.Combine(local, "ScanDesk", "Session"));
        File.WriteAllText(Path.Combine(local, "ScanDesk", "Session", "session.json"), "{}");

        LegacyDataMigration.Run(roaming, local);

        Assert.Equal("{\"old\":1}", File.ReadAllText(Path.Combine(roaming, "PrinterDesk", "settings.json")));
        Assert.True(File.Exists(Path.Combine(local, "PrinterDesk", "Session", "session.json")));
        Assert.False(Directory.Exists(Path.Combine(local, "ScanDesk", "Session")));
    }

    [Fact]
    public void Keeps_existing_data_under_the_new_name()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(Path.Combine(temp.Path, "ScanDesk"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "PrinterDesk"));
        File.WriteAllText(Path.Combine(temp.Path, "ScanDesk", "settings.json"), "old");
        File.WriteAllText(Path.Combine(temp.Path, "PrinterDesk", "settings.json"), "new");

        LegacyDataMigration.Run(temp.Path, temp.Path);

        Assert.Equal("new", File.ReadAllText(Path.Combine(temp.Path, "PrinterDesk", "settings.json")));
        Assert.True(File.Exists(Path.Combine(temp.Path, "ScanDesk", "settings.json")));
    }

    [Fact]
    public void Does_nothing_without_old_data()
    {
        using var temp = new TempFolder();

        LegacyDataMigration.Run(temp.Path, temp.Path);

        Assert.False(Directory.Exists(Path.Combine(temp.Path, "PrinterDesk")));
    }
}
