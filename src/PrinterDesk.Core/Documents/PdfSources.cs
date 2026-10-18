using PrinterDesk.Core.Pdfium;

namespace PrinterDesk.Core.Documents;

/// <summary>The PDFs used by the document's pages, opened once in PDFium and shared by thumbnails, printing and export.</summary>
public sealed class PdfSources : IDisposable
{
    private readonly Dictionary<string, Task<PdfiumDocument>> _open = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    public Task<PdfiumDocument> GetAsync(string path)
    {
        lock (_gate)
        {
            if (!_open.TryGetValue(path, out var task) || task.IsFaulted)
            {
                task = Task.Run(async () => await PdfiumDocument.OpenAsync(await File.ReadAllBytesAsync(path)));
                _open[path] = task;
            }
            return task;
        }
    }

    /// <summary>For background threads (print rendering); never call it on the PDFium thread.</summary>
    public PdfiumDocument Get(string path) => GetAsync(path).GetAwaiter().GetResult();

    /// <summary>Closes every PDF, e.g. when a new document is started.</summary>
    public void Clear()
    {
        List<Task<PdfiumDocument>> open;
        lock (_gate)
        {
            open = [.. _open.Values];
            _open.Clear();
        }
        foreach (var task in open.Where(t => t.IsCompletedSuccessfully))
            task.Result.Dispose();
    }

    public void Dispose() => Clear();
}
