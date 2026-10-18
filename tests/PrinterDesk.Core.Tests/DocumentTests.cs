using PdfSharp.Pdf.IO;
using PrinterDesk.Core.Documents;
using PrinterDesk.Core.Scanning;
using PrinterDesk.Core.Settings;

namespace PrinterDesk.Core.Tests;

public class DocumentTests
{
    [Fact]
    public void Pdf_has_one_page_per_scan_sized_by_dpi_and_rotated()
    {
        using var temp = new TempFolder();
        var session = new WorkingSession(temp.Path);
        var jpeg = Fixture.Bytes("page.jpg"); // 150x210 px
        var first = session.AddPage(jpeg, dpi: 150);
        var second = session.AddPage(jpeg, dpi: 150);
        second.Rotate(90);
        var pdfPath = Path.Combine(temp.Path, "out.pdf");

        PdfExporter.Export([first, second], pdfPath);

        using var pdf = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        Assert.Equal(2, pdf.PageCount);
        Assert.Equal(72, pdf.Pages[0].Width.Point, precision: 1);  // 150 px @ 150 dpi = 1 in
        Assert.Equal(100.8, pdf.Pages[0].Height.Point, precision: 1);
        Assert.Equal(90, pdf.Pages[1].Rotate);
    }

    [Fact]
    public void Exporting_no_pages_fails()
    {
        using var temp = new TempFolder();

        Assert.Throws<InvalidOperationException>(() => PdfExporter.Export([], Path.Combine(temp.Path, "out.pdf")));
    }

    [Fact]
    public void Session_restores_page_order_and_rotation()
    {
        using var temp = new TempFolder();
        var session = new WorkingSession(temp.Path);
        var a = session.AddPage(Fixture.Bytes("page.jpg"), 150);
        var b = session.AddPage(Fixture.Bytes("page.jpg"), 300);
        b.Rotate(-90);
        session.Save([b, a], isDirty: true);

        var (pages, dirty) = new WorkingSession(temp.Path).Load();

        Assert.True(dirty);
        Assert.Equal([b.FilePath, a.FilePath], pages.Select(p => p.FilePath));
        Assert.Equal(270, pages[0].Rotation);
        Assert.Equal(300, pages[0].Dpi);
    }

    [Fact]
    public void Session_skips_missing_files_and_clear_removes_everything()
    {
        using var temp = new TempFolder();
        var session = new WorkingSession(temp.Path);
        var a = session.AddPage(Fixture.Bytes("page.jpg"), 150);
        var b = session.AddPage(Fixture.Bytes("page.jpg"), 150);
        session.Save([a, b], isDirty: true);
        session.DeletePage(a);

        Assert.Single(session.Load().Pages);

        session.Clear();
        Assert.Empty(Directory.EnumerateFiles(temp.Path));
        Assert.Empty(session.Load().Pages);
    }

    [Fact]
    public void Settings_round_trip_and_default_to_150_dpi()
    {
        using var temp = new TempFolder();
        var store = new SettingsStore(Path.Combine(temp.Path, "sub", "settings.json"));

        Assert.Equal(150, store.Load().Scan.Dpi);

        store.Save(new AppSettings
        {
            ScannerUri = "http://192.168.1.130/eSCL/",
            Scan = new ScanOptions { Source = ScanSource.Feeder, Dpi = 300, Color = ColorMode.Grayscale },
        });
        var loaded = store.Load();

        Assert.Equal("http://192.168.1.130/eSCL/", loaded.ScannerUri);
        Assert.Equal(new ScanOptions { Source = ScanSource.Feeder, Dpi = 300, Color = ColorMode.Grayscale }, loaded.Scan);
        Assert.Contains("\"Grayscale\"", File.ReadAllText(store.Path));
    }

    [Fact]
    public void Corrupt_settings_fall_back_to_defaults()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, "{ not json");

        Assert.Equal(150, new SettingsStore(path).Load().Scan.Dpi);
    }
}
