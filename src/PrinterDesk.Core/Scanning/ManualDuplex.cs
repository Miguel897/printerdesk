namespace PrinterDesk.Core.Scanning;

/// <summary>
/// Manual duplex: the fronts are scanned first, then the stack is flipped and scanned again,
/// so the backs arrive in reverse order (back of the last sheet first).
/// </summary>
public static class ManualDuplex
{
    public static IReadOnlyList<T> Interleave<T>(IReadOnlyList<T> fronts, IReadOnlyList<T> backsAsScanned)
    {
        var backs = backsAsScanned.Reverse().ToList();
        var result = new List<T>(fronts.Count + backs.Count);
        var pairs = Math.Min(fronts.Count, backs.Count);

        for (var i = 0; i < pairs; i++)
        {
            result.Add(fronts[i]);
            result.Add(backs[i]);
        }

        // Count mismatch (misfeed, missing sheet): keep everything, extras at the end.
        result.AddRange(fronts.Skip(pairs));
        result.AddRange(backs.Skip(pairs));
        return result;
    }
}
