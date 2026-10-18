using PrinterDesk.Core.Escl;
using PrinterDesk.Core.Scanning;

namespace PrinterDesk.Core.Tests;

public class ScanPlannerTests
{
    private static readonly ScannerCapabilities Caps = ScannerCapabilities.Parse(Fixture.Text("ScannerCapabilities.xml"));
    private static readonly ScannerStatus Loaded = new("Idle", "ScannerAdfLoaded");
    private static readonly ScannerStatus Empty = new("Idle", "ScannerAdfEmpty");

    [Fact]
    public void Auto_uses_feeder_when_paper_is_loaded()
    {
        Assert.Equal(EsclSource.Feeder, ScanPlanner.Plan(new ScanOptions(), Caps, Loaded).Source);
        Assert.Equal(EsclSource.Platen, ScanPlanner.Plan(new ScanOptions(), Caps, Empty).Source);
    }

    [Fact]
    public void Feeder_resolution_is_capped_to_what_the_feeder_supports()
    {
        var request = ScanPlanner.Plan(new ScanOptions { Source = ScanSource.Feeder, Dpi = 600 }, Caps, Loaded);

        Assert.Equal(300, request.Dpi);
        Assert.Null(request.Region);
    }

    [Theory]
    [InlineData(PaperSize.A4, 2480, 3508)]
    [InlineData(PaperSize.Letter, 2550, 3300)]
    [InlineData(PaperSize.FullBed, 2550, 3508)]
    public void Flatbed_uses_paper_size_region(PaperSize paper, int width, int height)
    {
        var request = ScanPlanner.Plan(new ScanOptions { Source = ScanSource.Flatbed, Paper = paper }, Caps, Empty);

        Assert.Equal(new ScanRegion(width, height), request.Region);
        Assert.Equal(150, request.Dpi);
    }

    [Theory]
    [InlineData(ColorMode.Color, "RGB24")]
    [InlineData(ColorMode.Grayscale, "Grayscale8")]
    [InlineData(ColorMode.BlackAndWhite, "BlackAndWhite1")]
    public void Maps_color_modes(ColorMode color, string expected) =>
        Assert.Equal(expected, ScanPlanner.Plan(new ScanOptions { Color = color }, Caps, Empty).ColorMode);

    [Fact]
    public void Resolutions_offered_depend_on_source()
    {
        Assert.Equal([75, 100, 150, 200, 300], ScanPlanner.ResolutionsFor(ScanSource.Feeder, Caps));
        Assert.Equal(1200, ScanPlanner.ResolutionsFor(ScanSource.Flatbed, Caps).Max());
        Assert.Equal([75, 100, 150, 200, 300], ScanPlanner.ResolutionsFor(ScanSource.Auto, Caps));
    }

    [Fact]
    public void Scanner_without_feeder_cannot_scan_from_feeder()
    {
        var flatbedOnly = Caps with { Adf = null };

        Assert.Throws<EsclException>(() => ScanPlanner.Plan(new ScanOptions { Source = ScanSource.Feeder }, flatbedOnly, Loaded));
    }

    [Fact]
    public void Manual_duplex_interleaves_reversed_backs()
    {
        // Sheets A, B, C: fronts scanned A1 B1 C1; flipped stack gives C2 B2 A2.
        var result = ManualDuplex.Interleave(["A1", "B1", "C1"], ["C2", "B2", "A2"]);

        Assert.Equal(["A1", "A2", "B1", "B2", "C1", "C2"], result);
    }

    [Fact]
    public void Manual_duplex_keeps_extra_pages_on_count_mismatch()
    {
        var result = ManualDuplex.Interleave(["A1", "B1", "C1"], ["C2", "B2"]);

        Assert.Equal(5, result.Count);
        Assert.Equal(["A1", "B2", "B1", "C2", "C1"], result);
    }
}
