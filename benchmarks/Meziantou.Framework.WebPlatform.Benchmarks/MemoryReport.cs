using System.Runtime;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Benchmarks;

/// <summary>
/// Measures the memory of the parsed documents: the bytes retained by the document per node (elements, text, comments,
/// doctype) and the bytes allocated by the parser per node, compared with AngleSharp.
/// </summary>
internal static class MemoryReport
{
    public static int Run()
    {
        Console.WriteLine("| Document | Library | Nodes | Retained bytes/node | Allocated bytes/node |");
        Console.WriteLine("|---|---|---:|---:|---:|");
        foreach (var name in new[] { "small", "wikipedia", "html-spec" })
        {
            var html = BenchmarkData.Get(name);
            Measure(name, "Meziantou", () => HtmlDocumentParser.ParseDocument(html), document => CountNodes((Node)document));
            Measure(name, "AngleSharp", () => new global::AngleSharp.Html.Parser.HtmlParser().ParseDocument(html), document => CountNodes((global::AngleSharp.Dom.INode)document));
        }

        return 0;
    }

    private const int Documents = 3;

    private static void Measure(string name, string library, Func<object> parse, Func<object, int> countNodes)
    {
        // The first parse initializes the static data (tables, caches, pools) which is not part of the documents. The
        // retained bytes are the growth of the heap while several documents are alive.
        GC.KeepAlive(parse());
        var baseline = GetCompactedHeapSize();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var documents = new object[Documents];
        for (var i = 0; i < documents.Length; i++)
        {
            documents[i] = parse();
        }

        var allocated = (GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore) / Documents;
        var retained = (GetCompactedHeapSize() - baseline) / Documents;
        var nodes = countNodes(documents[0]);
        GC.KeepAlive(documents);
        Console.WriteLine($"| {name} | {library} | {nodes:N0} | {retained / (double)nodes:N0} | {allocated / (double)nodes:N0} |");
    }

    /// <summary>The size of the heap after a compacting collection (the free space between the objects is not counted).</summary>
    private static long GetCompactedHeapSize()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        return GC.GetTotalMemory(forceFullCollection: false);
    }

    private static int CountNodes(Node node)
    {
        var count = 1;
        for (var child = node.FirstChild; child is not null; child = child.NextSibling)
        {
            count += CountNodes(child);
        }

        if (node is HtmlTemplateElement template)
        {
            count += CountNodes(template.Content) - 1;
        }

        return count;
    }

    private static int CountNodes(global::AngleSharp.Dom.INode node)
    {
        var count = 1;
        for (var child = node.FirstChild; child is not null; child = child.NextSibling)
        {
            count += CountNodes(child);
        }

        if (node is global::AngleSharp.Html.Dom.IHtmlTemplateElement template)
        {
            count += CountNodes(template.Content) - 1;
        }

        return count;
    }
}
