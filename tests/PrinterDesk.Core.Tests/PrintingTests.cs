using System.Buffers.Binary;
using PrinterDesk.Core.Documents;
using PrinterDesk.Core.Ipp;
using PrinterDesk.Core.Printing;

namespace PrinterDesk.Core.Tests;

public class PageRangeTests
{
    [Theory]
    [InlineData("", new[] { 0, 1, 2, 3, 4 })]
    [InlineData("2", new[] { 1 })]
    [InlineData("1-3, 5", new[] { 0, 1, 2, 4 })]
    [InlineData("4-", new[] { 3, 4 })]
    [InlineData("-2", new[] { 0, 1 })]
    [InlineData("3,1", new[] { 2, 0 })]
    public void Valid_ranges(string text, int[] expected)
    {
        Assert.True(PageRange.TryParse(text, 5, out var pages, out _));
        Assert.Equal(expected, pages);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("6")]
    [InlineData("3-2")]
    [InlineData("a")]
    [InlineData(",")]
    public void Invalid_ranges_explain_the_problem(string text)
    {
        Assert.False(PageRange.TryParse(text, 5, out _, out var error));
        Assert.NotEmpty(error);
    }
}

public class MediaSizeTests
{
    [Theory]
    [InlineData("iso_a4_210x297mm", "A4", 210, 297)]
    [InlineData("na_letter_8.5x11in", "Letter", 215.9, 279.4)]
    [InlineData("na_index-4x6_4x6in", "4 × 6 in photo", 101.6, 152.4)]
    [InlineData("jpn_photo-2l_127x177.8mm", "2L photo (5 × 7 in)", 127, 177.8)]
    [InlineData("om_unknown_100x200mm", "unknown", 100, 200)]
    public void Keywords_describe_their_size(string keyword, string name, double width, double height)
    {
        var media = MediaSize.TryParse(keyword)!;

        Assert.Equal(name, media.Name);
        Assert.Equal(width, media.WidthMm, precision: 1);
        Assert.Equal(height, media.HeightMm, precision: 1);
    }

    [Theory]
    [InlineData("custom_min_3.5x5in")]
    [InlineData("custom_max_8.5x11.6928in")]
    [InlineData("a4")]
    public void Ranges_and_odd_names_are_skipped(string keyword) => Assert.Null(MediaSize.TryParse(keyword));
}

public class PrintLayoutTests
{
    // A4 at 300 dpi with 2.96 mm margins (35 px), like the HP ENVY.
    private static readonly SheetLayout A4 = SheetLayout.For(MediaSize.TryParse("iso_a4_210x297mm")!, 300, new Margins(296, 296, 296, 296));

    [Fact]
    public void Copies_are_collated_and_odd_copies_get_a_blank_back()
    {
        Assert.Equal([0, 1, 2, 0, 1, 2], PrintLayout.Sides([0, 1, 2], 2, twoSided: false));
        Assert.Equal([0, 1, 2, null, 0, 1, 2], PrintLayout.Sides([0, 1, 2], 2, twoSided: true));
        Assert.Equal([0, 1, 0, 1], PrintLayout.Sides([0, 1], 2, twoSided: true));
    }

    [Fact]
    public void Full_page_scan_shrinks_to_the_printable_area_centered()
    {
        // A4 scanned at 150 dpi: 1240 x 1754 px.
        var p = PrintLayout.Place(1240, 1754, 150, 0, PrintOrientation.Auto, A4);

        Assert.Equal(0, p.Rotation);
        Assert.True(p.Width <= A4.PrintableWidth && p.Height <= A4.PrintableHeight);
        Assert.Equal(A4.PrintableWidth, p.Width); // limited by width
        Assert.Equal(A4.Width - p.X - p.Width, p.X, tolerance: 1);
        Assert.Equal(A4.Height - p.Y - p.Height, p.Y, tolerance: 1);
    }

    [Fact]
    public void Small_page_keeps_its_real_size()
    {
        // A 4 x 2 in receipt at 300 dpi.
        var p = PrintLayout.Place(1200, 600, 300, 0, PrintOrientation.Portrait, A4);

        Assert.Equal((0, 1200, 600), (p.Rotation, p.Width, p.Height));
    }

    [Fact]
    public void Auto_turns_landscape_pages_counterclockwise()
    {
        var p = PrintLayout.Place(1754, 1240, 150, 0, PrintOrientation.Auto, A4);

        Assert.Equal(270, p.Rotation);
        Assert.True(p.Height > p.Width);
    }

    [Fact]
    public void User_rotation_counts_before_auto_orientation()
    {
        // Portrait scan the user turned to landscape: Auto turns it back up, so 90 + 270 = 0.
        var p = PrintLayout.Place(1240, 1754, 150, 90, PrintOrientation.Auto, A4);

        Assert.Equal(0, p.Rotation);
    }

    [Fact]
    public void Portrait_keeps_landscape_pages_unturned()
    {
        var p = PrintLayout.Place(1754, 1240, 150, 0, PrintOrientation.Portrait, A4);

        Assert.Equal(0, p.Rotation);
        Assert.True(p.Width > p.Height);
    }

    [Fact]
    public void Rotated_back_side_is_turned_and_mirrored_on_the_sheet()
    {
        var front = PrintLayout.Place(600, 300, 300, 0, PrintOrientation.Portrait, A4);
        var back = PrintLayout.Place(600, 300, 300, 0, PrintOrientation.Portrait, A4, rotateBackSide: true);

        Assert.Equal(180, back.Rotation);
        Assert.Equal(A4.Width - front.X - front.Width, back.X);
        Assert.Equal(A4.Height - front.Y - front.Height, back.Y);
    }

    [Theory]
    [InlineData(1, PrintSides.TwoSidedLongEdge, "rotated", true)]
    [InlineData(0, PrintSides.TwoSidedLongEdge, "rotated", false)]
    [InlineData(1, PrintSides.TwoSidedShortEdge, "rotated", false)]
    [InlineData(1, PrintSides.TwoSidedLongEdge, "normal", false)]
    [InlineData(1, PrintSides.OneSided, "rotated", false)]
    public void Back_side_rotation_rule(int side, PrintSides sides, string sheetBack, bool expected) =>
        Assert.Equal(expected, PrintLayout.IsBackSideRotated(side, sides, sheetBack));
}

public class PwgRasterWriterTests
{
    [Fact]
    public void Lines_round_trip_through_the_run_length_encoding()
    {
        byte[][] lines =
        [
            [1, 1, 1, 1],
            [1, 2, 3, 4],
            [1, 2, 2, 3],
            [5],
            [9, 9, 1, 2, 3, 3],
            Enumerable.Range(0, 300).Select(i => (byte)(i % 7 == 0 ? 0 : i)).ToArray(),
            Enumerable.Repeat((byte)255, 300).ToArray(),
        ];
        foreach (var line in lines)
        {
            var encoded = new byte[line.Length * 2 + 2];
            var length = PwgRasterWriter.EncodeLine(line, 1, encoded);

            Assert.Equal(line, PwgDecoder.DecodeLine(encoded.AsSpan(0, length), line.Length, 1));
        }
    }

    [Fact]
    public void Color_pages_round_trip_and_blank_lines_are_compressed()
    {
        const int width = 50, height = 40;
        var pixels = new byte[width * height * 3];
        pixels.AsSpan().Fill(0xFF);
        for (var x = 10; x < 20; x++)
            pixels[(5 * width + x) * 3 + 1] = (byte)(x * 10); // a gradient on line 5
        pixels[(7 * width + 3) * 3] = 0;                      // one cyan pixel on line 7
        var page = new PwgPage { Width = width, Height = height, Dpi = 300, Color = true, Duplex = true, TotalPageCount = 1, MediaKeyword = "iso_a4_210x297mm" };

        using var stream = new MemoryStream();
        new PwgRasterWriter(stream).WritePage(page, pixels);

        var decoded = PwgDecoder.Decode(stream.ToArray());
        var (header, data) = Assert.Single(decoded);
        Assert.Equal(pixels, data);
        Assert.Equal(19, header.ColorSpace);
        Assert.True(header.Duplex);
        Assert.Equal("iso_a4_210x297mm", header.PageSizeName);
        Assert.True(stream.Length < 1796 + 4 + 200, $"{stream.Length} bytes");
    }
}

public class PrintJobBuilderTests
{
    private static PrinterCapabilities Printer => PrinterCapabilities.Parse(IppMessage.Decode(Fixture.Bytes("printer-attributes.bin")));

    private static readonly ScanPage[] Pages =
    [
        new() { FilePath = "p1", Dpi = 150 },
        new() { FilePath = "p2", Dpi = 150 },
        new() { FilePath = "p3", Dpi = 150 },
    ];

    [Fact]
    public void Two_copies_of_three_pages_two_sided()
    {
        var rasterizer = new BlockRasterizer();
        var builder = new PrintJobBuilder(Pages, [0, 1, 2], new PrintOptions { Copies = 2, Sides = PrintSides.TwoSidedLongEdge, Color = PrintColor.Color }, Printer);

        using var stream = new MemoryStream();
        builder.Write(stream, rasterizer);
        var pages = PwgDecoder.Decode(stream.ToArray());

        Assert.Equal(7, builder.SideCount);
        Assert.Equal(4, builder.SheetCount);
        Assert.Equal(7, pages.Count);
        Assert.All(pages, p => Assert.Equal(7, p.Header.TotalPageCount));
        Assert.Equal(["p1", "p2", "p3", "p1", "p2", "p3"], rasterizer.Rendered.Select(r => r.Page));
        // Sides: p1 p2 p3 blank | p1 p2 p3. Backs (odd sides) holding p2 are turned 180°.
        Assert.Equal([0, 180, 0, 0, 180, 0], rasterizer.Rendered.Select(r => r.Placement.Rotation));
        Assert.All(pages[3].Pixels, b => Assert.Equal(0xFF, b));
        Assert.Equal("two-sided-long-edge", builder.Ticket.Sides);
        Assert.Equal("color", builder.Ticket.ColorMode);
        Assert.Equal(19, pages[0].Header.ColorSpace);
    }

    [Fact]
    public void Black_and_white_uses_gray_raster_and_paper_from_the_printer()
    {
        var builder = new PrintJobBuilder(Pages, [1], new PrintOptions { Color = PrintColor.BlackAndWhite, Media = "not-offered" }, Printer);

        using var stream = new MemoryStream();
        builder.Write(stream, new BlockRasterizer());
        var page = Assert.Single(PwgDecoder.Decode(stream.ToArray()));

        Assert.Equal(18, page.Header.ColorSpace);
        Assert.Equal((2480, 3508), (page.Header.Width, page.Header.Height));
        Assert.Equal("monochrome", builder.Ticket.ColorMode);
        Assert.Equal("iso_a4_210x297mm", builder.Ticket.Media);
        Assert.Contains(page.Pixels, b => b == 0); // the block was drawn
    }

    /// <summary>Pretends every page is A4 at 150 dpi and fills its placement with black.</summary>
    private sealed class BlockRasterizer : IPageRasterizer
    {
        public List<(string Page, PagePlacement Placement)> Rendered { get; } = [];

        public (int Width, int Height) GetPixelSize(ScanPage page) => (1240, 1754);

        public void Render(ScanPage page, PagePlacement placement, Span<byte> sheet, int sheetWidth, bool color)
        {
            Rendered.Add((page.FilePath, placement));
            var bpp = color ? 3 : 1;
            for (var y = placement.Y; y < placement.Y + placement.Height; y++)
                sheet.Slice((y * sheetWidth + placement.X) * bpp, placement.Width * bpp).Clear();
        }
    }
}

/// <summary>Reads PWG raster back, to check what the writer produced.</summary>
internal static class PwgDecoder
{
    public sealed record Header(int Width, int Height, int ColorSpace, bool Duplex, int TotalPageCount, string PageSizeName);

    public static List<(Header Header, byte[] Pixels)> Decode(byte[] data)
    {
        Assert.Equal("RaS2"u8.ToArray(), data[..4]);
        var pages = new List<(Header, byte[])>();
        var position = 4;
        while (position < data.Length)
        {
            var h = data.AsSpan(position, 1796).ToArray();
            int U(int offset) => BinaryPrimitives.ReadInt32BigEndian(h.AsSpan(offset));
            var name = System.Text.Encoding.ASCII.GetString(h, 1732, 64).TrimEnd('\0');
            var header = new Header(U(372), U(376), U(400), U(272) == 1, U(452), name);
            var bpp = U(388) / 8;
            position += 1796;

            var pixels = new byte[header.Width * header.Height * bpp];
            var y = 0;
            while (y < header.Height)
            {
                var repeat = data[position++] + 1;
                var line = DecodeLine(data.AsSpan(position), header.Width, bpp, out var used);
                position += used;
                for (var r = 0; r < repeat; r++, y++)
                    line.CopyTo(pixels.AsSpan(y * header.Width * bpp));
            }
            pages.Add((header, pixels));
        }
        return pages;
    }

    public static byte[] DecodeLine(ReadOnlySpan<byte> data, int width, int bpp) => DecodeLine(data, width, bpp, out _);

    private static byte[] DecodeLine(ReadOnlySpan<byte> data, int width, int bpp, out int used)
    {
        var line = new byte[width * bpp];
        int x = 0, i = 0;
        while (x < width)
        {
            var control = data[i++];
            if (control <= 127)
            {
                for (var r = 0; r <= control; r++, x++)
                    data.Slice(i, bpp).CopyTo(line.AsSpan(x * bpp));
                i += bpp;
            }
            else
            {
                var count = 257 - control;
                data.Slice(i, count * bpp).CopyTo(line.AsSpan(x * bpp));
                i += count * bpp;
                x += count;
            }
        }
        used = i;
        return line;
    }
}
