using System.Buffers.Binary;
using System.Text;

namespace PrinterDesk.Core.Printing;

/// <summary>Per-page settings for <see cref="PwgRasterWriter"/>.</summary>
public sealed record PwgPage
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int Dpi { get; init; }

    /// <summary>sRGB 8-bit (3 bytes per pixel) when true, else sGray 8-bit (1 byte per pixel).</summary>
    public required bool Color { get; init; }

    public bool Duplex { get; init; }

    /// <summary>Two-sided, flipped on the short edge.</summary>
    public bool Tumble { get; init; }

    public int TotalPageCount { get; init; }

    /// <summary>IPP print-quality: 3 draft, 4 normal, 5 high.</summary>
    public int Quality { get; init; } = 4;

    public string MediaKeyword { get; init; } = "";

    public int BytesPerPixel => Color ? 3 : 1;
}

/// <summary>
/// Writes PWG Raster (PWG 5102.4): the "RaS2" sync word, then per page a 1796-byte big-endian header
/// and the pixels, run-length encoded line by line.
/// </summary>
public sealed class PwgRasterWriter(Stream output)
{
    private const int HeaderSize = 1796;
    private const int ColorSpaceSGray = 18;
    private const int ColorSpaceSrgb = 19;
    private bool _started;

    /// <param name="pixels">Rows top to bottom, <see cref="PwgPage.BytesPerPixel"/> bytes per pixel, no padding.</param>
    public void WritePage(PwgPage page, ReadOnlySpan<byte> pixels)
    {
        var bpp = page.BytesPerPixel;
        var stride = page.Width * bpp;
        if (pixels.Length != stride * page.Height)
            throw new ArgumentException("Pixel buffer does not match the page size.", nameof(pixels));

        if (!_started)
        {
            output.Write("RaS2"u8);
            _started = true;
        }
        output.Write(Header(page));

        // Worst case per line: 1 repeat byte + 1 control byte per pixel + the pixels.
        var encoded = new byte[1 + page.Width * (bpp + 1)];
        var y = 0;
        while (y < page.Height)
        {
            var line = pixels.Slice(y * stride, stride);
            var repeat = 1;
            while (repeat < 256 && y + repeat < page.Height && pixels.Slice((y + repeat) * stride, stride).SequenceEqual(line))
                repeat++;

            encoded[0] = (byte)(repeat - 1);
            var length = 1 + EncodeLine(line, bpp, encoded.AsSpan(1));
            output.Write(encoded, 0, length);
            y += repeat;
        }
    }

    /// <summary>Control byte 0–127: the next pixel repeats n+1 times; 129–255: 257−n literal pixels follow.</summary>
    internal static int EncodeLine(ReadOnlySpan<byte> line, int bpp, Span<byte> destination)
    {
        var count = line.Length / bpp;
        var written = 0;
        var i = 0;
        while (i < count)
        {
            var run = 1;
            while (i + run < count && run < 128 && Pixel(line, i + run, bpp).SequenceEqual(Pixel(line, i, bpp)))
                run++;

            if (run > 1 || i == count - 1)
            {
                destination[written++] = (byte)(run - 1);
                Pixel(line, i, bpp).CopyTo(destination[written..]);
                written += bpp;
                i += run;
                continue;
            }

            // Literal: extend while the next pixel differs from the one after it.
            var literal = 1;
            while (i + literal < count && literal < 128 &&
                   (i + literal == count - 1 || !Pixel(line, i + literal, bpp).SequenceEqual(Pixel(line, i + literal + 1, bpp))))
            {
                literal++;
            }
            if (literal == 1)
            {
                destination[written++] = 0;
                Pixel(line, i, bpp).CopyTo(destination[written..]);
                written += bpp;
                i++;
                continue;
            }
            destination[written++] = (byte)(257 - literal);
            line.Slice(i * bpp, literal * bpp).CopyTo(destination[written..]);
            written += literal * bpp;
            i += literal;
        }
        return written;
    }

    private static ReadOnlySpan<byte> Pixel(ReadOnlySpan<byte> line, int index, int bpp) => line.Slice(index * bpp, bpp);

    internal static byte[] Header(PwgPage page)
    {
        var h = new byte[HeaderSize];
        void Text(int offset, string value) => Encoding.ASCII.GetBytes(value.AsSpan(0, Math.Min(value.Length, 63)), h.AsSpan(offset));
        void UInt(int offset, int value) => BinaryPrimitives.WriteInt32BigEndian(h.AsSpan(offset), value);

        Text(0, "PwgRaster");
        Text(192, "auto");                          // PrintContentOptimize
        UInt(272, page.Duplex ? 1 : 0);
        UInt(276, page.Dpi);                        // HWResolution
        UInt(280, page.Dpi);
        UInt(340, 1);                               // NumCopies (copies are rendered as pages)
        UInt(352, (int)Math.Round(page.Width * 72.0 / page.Dpi));  // PageSize in points
        UInt(356, (int)Math.Round(page.Height * 72.0 / page.Dpi));
        UInt(368, page.Tumble ? 1 : 0);
        UInt(372, page.Width);
        UInt(376, page.Height);
        UInt(384, 8);                               // BitsPerColor
        UInt(388, page.BytesPerPixel * 8);          // BitsPerPixel
        UInt(392, page.Width * page.BytesPerPixel); // BytesPerLine
        UInt(396, 0);                               // ColorOrder: chunky
        UInt(400, page.Color ? ColorSpaceSrgb : ColorSpaceSGray);
        UInt(420, page.BytesPerPixel);              // NumColors
        UInt(452, page.TotalPageCount);
        UInt(456, 1);                               // CrossFeedTransform: back sides are already turned
        UInt(460, 1);                               // FeedTransform
        UInt(480, 0xFFFFFF);                        // AlternatePrimary
        UInt(484, page.Quality);
        Text(1668, "auto");                         // RenderingIntent
        Text(1732, page.MediaKeyword);              // PageSizeName
        return h;
    }
}
