using PrinterDesk.Core.Scanning;

namespace PrinterDesk.Core.Tests;

public class ScanOptionsTests
{
    [Fact]
    public void Defaults_match_prd()
    {
        var options = new ScanOptions();

        Assert.Equal(150, options.Dpi);
        Assert.Equal(ScanSource.Auto, options.Source);
        Assert.Equal(ColorMode.Color, options.Color);
        Assert.Equal(PaperSize.A4, options.Paper);
    }
}
