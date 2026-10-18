using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace PrinterDesk.Core.Documents;

/// <summary>Builds a PDF with one page per scan. JPEGs are embedded as is (no recompression).</summary>
public static class PdfExporter
{
    public static void Export(IEnumerable<ScanPage> pages, string path)
    {
        var images = new List<XImage>();
        try
        {
            using var document = new PdfDocument();
            document.Info.Creator = "PrinterDesk";

            foreach (var page in pages)
            {
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
        }
    }
}
