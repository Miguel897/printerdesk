using System.Globalization;
using System.Windows.Data;
using PrinterDesk.Core.Scanning;

namespace PrinterDesk.App.Converters;

/// <summary>Friendly labels for the option enums shown in combo boxes.</summary>
public sealed class EnumDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ScanSource.Auto => "Auto (feeder if loaded)",
        ScanSource.Feeder => "Feeder",
        ScanSource.Flatbed => "Flatbed",
        ColorMode.Color => "Color",
        ColorMode.Grayscale => "Grayscale",
        ColorMode.BlackAndWhite => "Black & white",
        PaperSize.A4 => "A4",
        PaperSize.Letter => "Letter",
        PaperSize.FullBed => "Full bed",
        int dpi => $"{dpi} dpi",
        _ => value?.ToString(),
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
