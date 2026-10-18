using PrinterDesk.Core.Escl;

namespace PrinterDesk.Core.Tests;

public class EsclParsingTests
{
    [Fact]
    public void Parses_envy_capabilities()
    {
        var caps = ScannerCapabilities.Parse(Fixture.Text("ScannerCapabilities.xml"));

        Assert.Equal("ENVY 6400 All-in-One Printer series", caps.MakeAndModel);
        Assert.Equal(35, caps.FeederCapacity);

        Assert.NotNull(caps.Platen);
        Assert.Equal([75, 100, 150, 200, 300, 400, 600, 1200], caps.Platen.Resolutions);
        Assert.Equal(2550, caps.Platen.MaxWidth);
        Assert.Equal(3508, caps.Platen.MaxHeight);
        Assert.Contains("image/jpeg", caps.Platen.DocumentFormats);

        Assert.NotNull(caps.Adf);
        Assert.Equal([75, 100, 150, 200, 300], caps.Adf.Resolutions);
        Assert.Equal(["BlackAndWhite1", "Grayscale8", "RGB24"], caps.Adf.ColorModes);
    }

    [Theory]
    [InlineData(150, 150)]
    [InlineData(600, 300)]
    [InlineData(250, 200)]
    [InlineData(50, 75)]
    public void Closest_resolution_never_exceeds_request_except_below_minimum(int requested, int expected)
    {
        var adf = ScannerCapabilities.Parse(Fixture.Text("ScannerCapabilities.xml")).Adf!;

        Assert.Equal(expected, adf.ClosestResolution(requested));
    }

    [Fact]
    public void Parses_status()
    {
        var status = ScannerStatus.Parse(Fixture.Text("ScannerStatus.xml"));

        Assert.True(status.IsIdle);
        Assert.False(status.IsAdfLoaded);
        Assert.False(status.HasAdfProblem);
        Assert.Equal("Idle · Feeder empty", status.Describe());
    }

    [Theory]
    [InlineData("ScannerAdfJam", true)]
    [InlineData("ScannerAdfHatchOpen", true)]
    [InlineData("ScannerAdfLoaded", false)]
    [InlineData(null, false)]
    public void Detects_feeder_problems(string? adfState, bool expected) =>
        Assert.Equal(expected, new ScannerStatus("Idle", adfState).HasAdfProblem);

    [Fact]
    public void Scan_request_xml_has_settings_and_optional_region()
    {
        var flatbed = new ScanRequest(EsclSource.Platen, 150, "RGB24", Region: new ScanRegion(2480, 3508)).ToXml();
        var feeder = new ScanRequest(EsclSource.Feeder, 300, "Grayscale8").ToXml();

        Assert.Contains("<pwg:InputSource>Platen</pwg:InputSource>", flatbed);
        Assert.Contains("<scan:XResolution>150</scan:XResolution>", flatbed);
        Assert.Contains("<pwg:Width>2480</pwg:Width>", flatbed);
        Assert.Contains("<pwg:DocumentFormat>image/jpeg</pwg:DocumentFormat>", flatbed);

        Assert.Contains("<pwg:InputSource>Feeder</pwg:InputSource>", feeder);
        Assert.Contains("<scan:ColorMode>Grayscale8</scan:ColorMode>", feeder);
        Assert.DoesNotContain("ScanRegions", feeder);
    }
}
