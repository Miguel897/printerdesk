using System.Globalization;
using System.Text.RegularExpressions;

namespace PrinterDesk.Core.Printing;

/// <summary>A paper size from a self-describing PWG media keyword such as <c>iso_a4_210x297mm</c>.</summary>
public sealed partial record MediaSize(string Keyword, string Name, double WidthMm, double HeightMm)
{
    private static readonly Dictionary<string, string> FriendlyNames = new()
    {
        ["iso_a4"] = "A4",
        ["iso_a5"] = "A5",
        ["iso_a6"] = "A6",
        ["iso_b5"] = "B5",
        ["jis_b5"] = "B5 (JIS)",
        ["na_letter"] = "Letter",
        ["na_executive"] = "Executive",
        ["na_govt-letter"] = "8 × 10 in",
        ["na_index-4x6"] = "4 × 6 in photo",
        ["na_index-5x8"] = "5 × 8 in card",
        ["na_5x7"] = "5 × 7 in photo",
        ["om_small-photo"] = "10 × 15 cm photo",
        ["oe_photo-l"] = "L photo (3.5 × 5 in)",
        ["jpn_photo-2l"] = "2L photo (5 × 7 in)",
        ["jpn_hagaki"] = "Hagaki postcard",
        ["na_number-10"] = "Envelope #10",
        ["iso_dl"] = "Envelope DL",
        ["iso_c6"] = "Envelope C6",
        ["jpn_chou3"] = "Envelope Chou #3",
        ["jpn_chou4"] = "Envelope Chou #4",
    };

    /// <summary>Parses class_name_WxHunit; custom_min/custom_max ranges and unknown units return null.</summary>
    public static MediaSize? TryParse(string keyword)
    {
        var match = KeywordPattern().Match(keyword);
        if (!match.Success || match.Groups["prefix"].Value.StartsWith("custom", StringComparison.Ordinal))
            return null;

        var factor = match.Groups["unit"].Value == "in" ? 25.4 : 1.0;
        var width = double.Parse(match.Groups["w"].Value, CultureInfo.InvariantCulture) * factor;
        var height = double.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture) * factor;
        var prefix = match.Groups["prefix"].Value;
        var name = FriendlyNames.TryGetValue(prefix, out var friendly) ? friendly : prefix[(prefix.IndexOf('_') + 1)..];
        return new MediaSize(keyword, name, width, height);
    }

    public override string ToString() => $"{Name} ({Format(WidthMm)} × {Format(HeightMm)} mm)";

    private static string Format(double mm) => Math.Round(mm).ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(?<prefix>[a-z0-9]+_[a-z0-9.\-]+)_(?<w>\d+(\.\d+)?)x(?<h>\d+(\.\d+)?)(?<unit>mm|in)$")]
    private static partial Regex KeywordPattern();
}
