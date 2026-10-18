using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PrinterDesk.Core.Documents;

/// <summary>
/// Builds a PDF with one page per document page. Scanned JPEGs are embedded as is (no recompression);
/// pages of opened PDFs are copied as they are (vector text and graphics stay sharp).
/// </summary>
public static class PdfExporter
{
    public static void Export(IEnumerable<DocumentPage> pages, string path)
    {
        var images = new List<XImage>();
        var sources = new Dictionary<string, PdfDocument>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var document = new PdfDocument();
            document.Info.Creator = "PrinterDesk";

            foreach (var page in pages)
            {
                if (page.PdfPageIndex is { } index)
                {
                    if (!sources.TryGetValue(page.FilePath, out var source))
                        sources[page.FilePath] = source = PdfReader.Open(page.FilePath, PdfDocumentOpenMode.Import);
                    var imported = document.AddPage(source.Pages[index]);
                    imported.Rotate = ((imported.Rotate + page.Rotation) % 360 + 360) % 360;
                    continue;
                }

                var image = XImage.FromFile(page.FilePath);
                images.Add(image);

                // Page size follows the scan: pixels / dpi = inches, 72 points per inch.
                var width = image.PixelWidth * 72.0 / page.Dpi;
                var height = image.PixelHeight * 72.0 / page.Dpi;

                var pdfPage = document.AddPage();
                pdfPage.Width = XUnit.FromPoint(width);
                pdfPage.Height = XUnit.FromPoint(height);
                using (var gfx = XGraphics.FromPdfPage(pdfPage))
                    gfx.DrawImage(image, 0, 0, width, height);
                pdfPage.Rotate = page.Rotation;
            }

            if (document.PageCount == 0)
                throw new InvalidOperationException("There are no pages to export.");

            document.Save(path);
        }
        finally
        {
            foreach (var image in images)
                image.Dispose();
            foreach (var source in sources.Values)
                source.Dispose();
        }
    }
}
