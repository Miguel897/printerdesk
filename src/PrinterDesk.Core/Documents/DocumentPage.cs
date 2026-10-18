namespace PrinterDesk.Core.Documents;

/// <summary>
/// One page of the document: a scanned or opened image (JPEG/PNG), or a page of an opened PDF,
/// plus a view rotation. Files live in the working session folder.
/// </summary>
public sealed class DocumentPage
{
    /// <summary>Points per inch: PDF pages use <see cref="Dpi"/> = 72, so their "pixels" are points.</summary>
    public const int PdfDpi = 72;

    public required string FilePath { get; init; }

    public required int Dpi { get; init; }

    /// <summary>0-based page of the PDF at <see cref="FilePath"/>; null for an image page.</summary>
    public int? PdfPageIndex { get; init; }

    public bool IsPdf => PdfPageIndex is not null;

    /// <summary>Clockwise rotation in degrees: 0, 90, 180 or 270.</summary>
    public int Rotation { get; set; }

    public void Rotate(int degrees) => Rotation = ((Rotation + degrees) % 360 + 360) % 360;
}
