using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PrinterDesk.Core.Documents;
using PrinterDesk.Core.Pdfium;

namespace PrinterDesk.Core.Tests;

public class OpenFileTests
{
    /// <summary>Two pages: 200 × 100 pt with a black left half, then A4 portrait.</summary>
    private static string MakePdf(string folder, string? password = null)
    {
        var path = Path.Combine(folder, password is null ? "input.pdf" : "locked.pdf");
        using var document = new PdfDocument();
        var first = document.AddPage();
        first.Width = XUnit.FromPoint(200);
        first.Height = XUnit.FromPoint(100);
        using (var gfx = XGraphics.FromPdfPage(first))
            gfx.DrawRectangle(XBrushes.Black, 0, 0, 100, 100);
        document.AddPage().Size = PdfSharp.PageSize.A4;
        if (password is not null)
            document.SecuritySettings.UserPassword = password;
        document.Save(path);
        return path;
    }

    [Fact]
    public async Task Pdfium_reads_sizes_and_renders_pages()
    {
        using var temp = new TempFolder();
        using var pdf = await PdfiumDocument.OpenAsync(File.ReadAllBytes(MakePdf(temp.Path)));

        Assert.Equal(2, pdf.PageCount);
        Assert.Equal((200, 100), (pdf.PageSizes[0].Width, pdf.PageSizes[0].Height));

        var bitmap = await pdf.RenderAsync(0, 0, 40, 20, forPrinting: true);
        Assert.Equal((40, 20), (bitmap.Width, bitmap.Height));
        Assert.Equal(0, Pixel(bitmap, 5, 10));    // black half
        Assert.Equal(255, Pixel(bitmap, 35, 10)); // white half

        var turned = await pdf.RenderAsync(0, 1, 20, 40, forPrinting: false);
        Assert.Equal(0, Pixel(turned, 10, 5));    // a quarter turn clockwise puts the black half on top
        Assert.Equal(255, Pixel(turned, 10, 35));
    }

    private static int Pixel(RenderedBitmap bitmap, int x, int y) => bitmap.Pixels[y * bitmap.Stride + x * 4];

    [Fact]
    public async Task Pdf_import_adds_one_page_per_pdf_page_from_a_session_copy()
    {
        using var temp = new TempFolder();
        var source = MakePdf(temp.Path);
        var session = new WorkingSession(Path.Combine(temp.Path, "session"));
        using var pdfs = new PdfSources();

        var pages = await DocumentImporter.ImportAsync(source, session, pdfs);

        Assert.Equal([0, 1], pages.Select(p => p.PdfPageIndex!.Value));
        Assert.All(pages, p => Assert.StartsWith(session.Folder, p.FilePath));
        Assert.All(pages, p => Assert.Equal(DocumentPage.PdfDpi, p.Dpi));
        File.Delete(source);
        Assert.True(File.Exists(pages[0].FilePath)); // the document does not depend on the original
    }

    [Fact]
    public async Task Image_import_gets_a_dpi_that_fits_a4()
    {
        using var temp = new TempFolder();
        var session = new WorkingSession(temp.Path);
        using var pdfs = new PdfSources();

        var page = Assert.Single(await DocumentImporter.ImportAsync(Fixture.PathOf("page.jpg"), session, pdfs));

        Assert.False(page.IsPdf);
        Assert.True(page.Dpi >= 50);
    }

    [Theory]
    [InlineData(4000, 3000, 72, 363)]  // phone photo: raised so the short side fits 210 mm
    [InlineData(2480, 3508, 300, 300)] // A4 scan keeps its resolution
    [InlineData(800, 600, 0, 96)]      // no resolution in the file
    public void Image_dpi(int width, int height, double declared, int expected) =>
        Assert.Equal(expected, DocumentImporter.ImageDpi(width, height, declared));

    [Fact]
    public async Task Password_protected_and_unsupported_files_are_refused_and_not_kept()
    {
        using var temp = new TempFolder();
        var session = new WorkingSession(Path.Combine(temp.Path, "session"));
        using var pdfs = new PdfSources();
        var text = Path.Combine(temp.Path, "notes.txt");
        File.WriteAllText(text, "hello");

        var locked = await Assert.ThrowsAsync<ImportException>(() => DocumentImporter.ImportAsync(MakePdf(temp.Path, "secret"), session, pdfs));
        var unsupported = await Assert.ThrowsAsync<ImportException>(() => DocumentImporter.ImportAsync(text, session, pdfs));

        Assert.Contains("password", locked.Message);
        Assert.Contains("only PDF, JPEG and PNG", unsupported.Message);
        Assert.Empty(Directory.EnumerateFiles(session.Folder));
    }

    [Fact]
    public async Task Pdf_pages_are_exported_as_pages_with_the_user_rotation()
    {
        using var temp = new TempFolder();
        var session = new WorkingSession(Path.Combine(temp.Path, "session"));
        using var pdfs = new PdfSources();
        var pdfPages = await DocumentImporter.ImportAsync(MakePdf(temp.Path), session, pdfs);
        var scan = session.AddPage(Fixture.Bytes("page.jpg"), 150);
        pdfPages[0].Rotate(90);
        var output = Path.Combine(temp.Path, "out.pdf");

        PdfExporter.Export([pdfPages[0], scan, pdfPages[1]], output);

        using var result = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.Equal(3, result.PageCount);
        Assert.Equal(90, result.Pages[0].Rotate);
        Assert.Equal(200, result.Pages[0].Width.Point, precision: 1); // the page itself, not a picture of it
        Assert.Equal(595, result.Pages[2].Width.Point, precision: 0);
    }

    [Fact]
    public async Task Session_restores_pdf_pages_and_keeps_shared_pdf_until_cleared()
    {
        using var temp = new TempFolder();
        var session = new WorkingSession(Path.Combine(temp.Path, "session"));
        using var pdfs = new PdfSources();
        var pages = await DocumentImporter.ImportAsync(MakePdf(temp.Path), session, pdfs);
        pages[1].Rotate(180);

        session.DeletePage(pages[0]);
        session.Save([pages[1]], isDirty: true);
        var (restored, dirty) = session.Load();

        var page = Assert.Single(restored);
        Assert.True(dirty);
        Assert.Equal((1, 180), (page.PdfPageIndex, page.Rotation));
        Assert.True(File.Exists(page.FilePath));
    }
}
