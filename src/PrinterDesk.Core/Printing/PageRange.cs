namespace PrinterDesk.Core.Printing;

public static class PageRange
{
    /// <summary>
    /// Parses "1-3, 5, 8-" (1-based, open ends allowed) into 0-based page indexes in the order given.
    /// Empty text means all pages.
    /// </summary>
    /// <returns>False with a message for the user when the text is invalid.</returns>
    public static bool TryParse(string? text, int pageCount, out IReadOnlyList<int> pages, out string error)
    {
        pages = [];
        error = "";
        if (string.IsNullOrWhiteSpace(text))
        {
            pages = Enumerable.Range(0, pageCount).ToList();
            return pageCount > 0 || Fail("There are no pages to print.", out error);
        }

        var result = new List<int>();
        foreach (var part in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var dash = part.IndexOf('-');
            var first = dash < 0 ? part : part[..dash].Trim();
            var last = dash < 0 ? part : part[(dash + 1)..].Trim();
            if (!TryPage(first.Length == 0 ? "1" : first, out var from) ||
                !TryPage(last.Length == 0 ? pageCount.ToString() : last, out var to))
            {
                return Fail($"\"{part}\" is not a page number or range. Example: 1-3, 5", out error);
            }
            if (from > to)
                return Fail($"\"{part}\": the first page is after the last one.", out error);
            if (to > pageCount)
                return Fail($"\"{part}\": the document has only {pageCount} page{(pageCount == 1 ? "" : "s")}.", out error);
            for (var page = from; page <= to; page++)
                result.Add(page - 1);
        }

        if (result.Count == 0)
            return Fail("Type the pages to print, e.g. 1-3, 5", out error);
        pages = result;
        return true;
    }

    private static bool TryPage(string text, out int page) => int.TryParse(text, out page) && page >= 1;

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}
