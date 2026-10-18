namespace PrinterDesk.Core.Documents;

/// <summary>One scanned page: the JPEG received from the scanner plus a view rotation.</summary>
public sealed class ScanPage
{
    public required string FilePath { get; init; }

    public required int Dpi { get; init; }

    /// <summary>Clockwise rotation in degrees: 0, 90, 180 or 270.</summary>
    public int Rotation { get; set; }

    public void Rotate(int degrees) => Rotation = ((Rotation + degrees) % 360 + 360) % 360;
}
