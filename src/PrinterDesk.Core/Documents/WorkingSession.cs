using System.Text.Json;

namespace PrinterDesk.Core.Documents;

/// <summary>
/// Folder holding the pages of the document being built, plus a manifest with their order,
/// so a crash or accidental close does not lose a long feeder batch.
/// </summary>
public sealed class WorkingSession
{
    private const string ManifestName = "session.json";
    private const string PagePrefix = "page_";
    private const string PreviewPrefix = "preview_";
    private const string SourcePrefix = "source_";

    public WorkingSession(string folder)
    {
        Folder = folder;
        Directory.CreateDirectory(folder);
    }

    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrinterDesk", "Session");

    public string Folder { get; }

    public DocumentPage AddPage(byte[] jpeg, int dpi)
    {
        var path = Path.Combine(Folder, $"{PagePrefix}{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.jpg");
        File.WriteAllBytes(path, jpeg);
        return new DocumentPage { FilePath = path, Dpi = dpi };
    }

    /// <summary>Copies an opened image or PDF into the session, so the document does not depend on the original file.</summary>
    public string AddSourceFile(string path)
    {
        var copy = Path.Combine(Folder, $"{SourcePrefix}{Guid.NewGuid():N}{Path.GetExtension(path).ToLowerInvariant()}");
        File.Copy(path, copy);
        return copy;
    }

    /// <summary>A PDF is shared by its pages, so it stays until <see cref="Clear"/>.</summary>
    public void DeletePage(DocumentPage page)
    {
        if (!page.IsPdf)
            TryDelete(page.FilePath);
    }

    public void Save(IEnumerable<DocumentPage> pages, bool isDirty)
    {
        var manifest = new Manifest(isDirty, pages.Select(p => new ManifestPage(Path.GetFileName(p.FilePath), p.Dpi, p.Rotation, p.PdfPageIndex)).ToList());
        var temp = Path.Combine(Folder, ManifestName + ".tmp");
        File.WriteAllText(temp, JsonSerializer.Serialize(manifest));
        File.Move(temp, Path.Combine(Folder, ManifestName), overwrite: true);
    }

    /// <summary>Pages left from a previous run (missing files are skipped).</summary>
    public (IReadOnlyList<DocumentPage> Pages, bool IsDirty) Load()
    {
        var path = Path.Combine(Folder, ManifestName);
        if (!File.Exists(path))
            return ([], false);

        try
        {
            var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path));
            if (manifest is null)
                return ([], false);

            var pages = manifest.Pages
                .Select(p => new DocumentPage { FilePath = Path.Combine(Folder, p.File), Dpi = p.Dpi, Rotation = p.Rotation, PdfPageIndex = p.PdfPage })
                .Where(p => File.Exists(p.FilePath))
                .ToList();
            return (pages, manifest.IsDirty);
        }
        catch (JsonException)
        {
            return ([], false);
        }
    }

    /// <summary>Deletes all pages, previews and the manifest.</summary>
    public void Clear()
    {
        foreach (var file in Directory.EnumerateFiles(Folder))
            TryDelete(file);
    }

    public string NewPreviewPath() => Path.Combine(Folder, $"{PreviewPrefix}{DateTime.Now:HHmmss_fff}.pdf");

    /// <summary>Deletes old preview PDFs; files still open in the viewer are left for later.</summary>
    public void DeletePreviewsExcept(string? keep)
    {
        foreach (var file in Directory.EnumerateFiles(Folder, PreviewPrefix + "*.pdf"))
        {
            if (!string.Equals(file, keep, StringComparison.OrdinalIgnoreCase))
                TryDelete(file);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record Manifest(bool IsDirty, List<ManifestPage> Pages);

    private sealed record ManifestPage(string File, int Dpi, int Rotation, int? PdfPage = null);
}
