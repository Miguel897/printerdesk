using System.Collections.Concurrent;

namespace PrinterDesk.Core.Pdfium;

/// <summary>PDFium is not thread-safe: every call goes through this single background thread.</summary>
public static class PdfiumThread
{
    private static readonly BlockingCollection<Action> Queue = new();
    private static readonly Thread Worker;

    static PdfiumThread()
    {
        // The worker only touches its parameter, never this type's statics (that would deadlock
        // on the type initializer lock while this constructor is still running).
        Queue.Add(PdfiumNative.FPDF_InitLibrary);
        Worker = new Thread(Run) { IsBackground = true, Name = "PDFium" };
        Worker.Start(Queue);
    }

    public static Task<T> Invoke<T>(Func<T> work)
    {
        if (Thread.CurrentThread == Worker)
            return Task.FromResult(work());

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue.Add(() =>
        {
            try
            {
                tcs.SetResult(work());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    public static Task Invoke(Action work) => Invoke(() =>
    {
        work();
        return true;
    });

    private static void Run(object? queue)
    {
        foreach (var work in ((BlockingCollection<Action>)queue!).GetConsumingEnumerable())
            work();
    }
}
