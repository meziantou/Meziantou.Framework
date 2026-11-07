using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Benchmarks;

/// <summary>
/// Lays out a table of 10,000 rows whose first cell spans all the rows (as the WPT test
/// <c>html/semantics/tabular-data/processing-model-1/span-limits.html</c> does with 65,534 rows): each iteration loads a new
/// document (not measured), then the styles, the box tree and the layout of the table are computed.
/// </summary>
[MemoryDiagnoser]
public class TableBenchmarks
{
    private const int RowCount = 10_000;

    private string _html = "";
    private BrowsingContext? _context;
    private Document _document = null!;

    [GlobalSetup]
    public void Setup() => _html = TableDocument.Create(RowCount);

    [IterationSetup]
    public void SetupDocument()
    {
        _context?.Dispose();
        _context = new BrowsingContext();
        _document = _context.Navigate("https://example.com/", _ => _html);
    }

    [GlobalCleanup]
    public void Cleanup() => _context?.Dispose();

    /// <summary>Computes the styles and the layout of the table.</summary>
    [Benchmark]
    public object? LayoutLargeTable() => _document.GetLayoutEngine().EnsureLayout();
}

/// <summary>The documents of <see cref="TableBenchmarks"/>.</summary>
internal static class TableDocument
{
    /// <summary>Creates a document with a table of <paramref name="rowCount"/> rows whose first cell spans all the rows.</summary>
    public static string Create(int rowCount)
    {
        var html = new StringBuilder(rowCount * 10 + 200);
        html.Append("<!DOCTYPE html><html><body><table border=1><tr><td rowspan=").Append(rowCount.ToString(CultureInfo.InvariantCulture)).Append(" id=first>a<td>");
        for (var i = 1; i < rowCount; i++)
        {
            html.Append("<tr><td>");
        }

        html.Append("</table></body></html>");
        return html.ToString();
    }
}

/// <summary>Lays out the document of <see cref="TableBenchmarks"/> in a loop, to profile it (e.g. with <c>dotnet-trace</c>).</summary>
internal static class TableProfile
{
    public static int Run(int rowCount, int count)
    {
        var html = TableDocument.Create(rowCount);
        var cpuTotal = TimeSpan.Zero;
        var elapsedTotal = TimeSpan.Zero;
        var allocated = 0L;
        for (var i = 0; i <= count; i++)
        {
            using var context = new BrowsingContext();
            var document = context.Navigate("https://example.com/", _ => html);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var cpu = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            _ = document.GetLayoutEngine().EnsureLayout();

            // (the first iteration warms up the code)
            if (i > 0)
            {
                elapsedTotal += stopwatch.Elapsed;
                cpuTotal += System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime - cpu;
                allocated += GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            }
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"table {rowCount}: {elapsedTotal.TotalMilliseconds / count:F1} ms per layout, {cpuTotal.TotalMilliseconds / count:F1} ms CPU, {allocated / count / 1024 / 1024.0:F1} MB allocated"));
        return 0;
    }
}
