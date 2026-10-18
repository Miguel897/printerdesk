using PdfSharp.Drawing;
using PrinterDesk.Core.Pdfium;

namespace PrinterDesk.Core.Documents;

public sealed class ImportException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Adds opened PDF, JPEG and PNG files to the document.</summary>
public static class DocumentImporter
{
    public static readonly string[] Extensions = [".pdf", ".jpg", ".jpeg", ".png"];

    public const string DialogFilter = "PDF and images (*.pdf;*.jpg;*.jpeg;*.png)|*.pdf;*.jpg;*.jpeg;*.png|All files (*.*)|*.*";

    public static bool IsSupported(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Copies the file into the session and returns its pages: one per PDF page, or one for an image.</summary>
    public static async Task<IReadOnlyList<DocumentPage>> ImportAsync(string path, WorkingSession session, PdfSources pdfs)
    {
        var name = Path.GetFileName(path);
        if (!IsSupported(path))
            throw new ImportException($"{name}: only PDF, JPEG and PNG files can be opened.");

        string copy;
        try
        {
            copy = session.AddSourceFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ImportException($"{name}: {ex.Message}", ex);
        }

        try
        {
            if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                var pdf = await pdfs.GetAsync(copy);
                if (pdf.PageCount == 0)
                    throw new ImportException($"{name}: the PDF has no pages.");
                return Enumerable.Range(0, pdf.PageCount)
                    .Select(i => new DocumentPage { FilePath = copy, Dpi = DocumentPage.PdfDpi, PdfPageIndex = i })
                    .ToList();
            }

            using var image = XImage.FromFile(copy);
            return [new DocumentPage { FilePath = copy, Dpi = ImageDpi(image.PixelWidth, image.PixelHeight, image.HorizontalResolution) }];
        }
        catch (Exception ex) when (ex is ImportException or PdfOpenException or InvalidOperationException or IOException or NotSupportedException)
        {
            File.Delete(copy);
            if (ex is ImportException)
                throw;
            throw new ImportException($"{name}: {(ex is PdfOpenException ? ex.Message : "the file could not be read.")}", ex);
        }
    }

    /// <summary>
    /// The image's own resolution (96 dpi when missing), raised when needed so the page is at most
    /// A4 size: a 4000 px phone photo at 72 dpi would otherwise be a 1.4 m wide page.
    /// </summary>
    public static int ImageDpi(int pixelWidth, int pixelHeight, double declaredDpi)
    {
        const double a4Short = 210 / 25.4, a4Long = 297 / 25.4;
        var dpi = declaredDpi >= 50 ? declaredDpi : 96;
        var needed = Math.Max(Math.Max(pixelWidth, pixelHeight) / a4Long, Math.Min(pixelWidth, pixelHeight) / a4Short);
        // 1 % tolerance: an A4 scan at 300 dpi (2480 × 3508 px) is A4 within rounding and keeps its 300 dpi.
        return (int)Math.Ceiling(needed > dpi * 1.01 ? needed : dpi);
    }
}
