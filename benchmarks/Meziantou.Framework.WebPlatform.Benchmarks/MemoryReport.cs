using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Benchmarks;

/// <summary>
/// Measures the memory of the parsed documents: the size of the node objects, the bytes retained by the document per node
/// (elements, text, comments, doctype), the bytes allocated by the parser per node, and the bytes retained by the computed
/// styles per element.
/// </summary>
internal static class MemoryReport
{
    public static int Run()
    {
        Console.WriteLine("| Type | Instance bytes |");
        Console.WriteLine("|---|---:|");
        foreach (var name in new[] { "Dom.Text", "Dom.Comment", "Dom.Element", "Dom.HtmlDivElement", "Dom.Attr", "Dom.NodeRareData", "Dom.ElementRareData" })
        {
            if (typeof(Node).Assembly.GetType("Meziantou.Framework.WebPlatform." + name) is { } type)
            {
                Console.WriteLine($"| {name} | {GetInstanceSize(type):N0} |");
            }
        }

        Console.WriteLine();
        Console.WriteLine("| Document | Nodes | Retained bytes/node | Allocated bytes/node | Elements with rare data |");
        Console.WriteLine("|---|---:|---:|---:|---:|");
        foreach (var name in new[] { "small", "wikipedia", "html-spec" })
        {
            var html = BenchmarkData.Get(name);
            Measure(name, () => HtmlDocumentParser.ParseDocument(html));
        }

        Console.WriteLine();
        Console.WriteLine("| Document | Properties | Elements | Retained style bytes/element | Elements with rare data |");
        Console.WriteLine("|---|---|---:|---:|---:|");
        foreach (var name in new[] { "wikipedia-styled", "small-bootstrap" })
        {
            var html = BenchmarkData.Get(name);
            MeasureStyles(name, html, allProperties: false);
            MeasureStyles(name, html, allProperties: true);
        }

        return 0;
    }

    private const int Documents = 3;

    private static readonly string[] Properties = ["display", "color", "font-size", "margin-top"];

    private static void Measure(string name, Func<Document> parse)
    {
        // The first parse initializes the static data (tables, caches, pools) which is not part of the documents. The
        // retained bytes are the growth of the heap while several documents are alive.
        GC.KeepAlive(parse());
        var baseline = GetCompactedHeapSize();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var documents = new Document[Documents];
        for (var i = 0; i < documents.Length; i++)
        {
            documents[i] = parse();
        }

        var allocated = (GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore) / Documents;
        var retained = (GetCompactedHeapSize() - baseline) / Documents;
        var nodes = CountNodes(documents[0]);
        GC.KeepAlive(documents);
        Console.WriteLine($"| {name} | {nodes:N0} | {retained / (double)nodes:N0} | {allocated / (double)nodes:N0} | {FormatRareData(documents[0])} |");
    }

    /// <summary>The bytes retained by the computed styles of all the elements of a document (the document itself is not counted).</summary>
    private static void MeasureStyles(string name, string html, bool allProperties)
    {
        // The first document initializes the static data
        ComputeStyles(Load(html, out var warmup), allProperties);
        warmup.Dispose();

        var contexts = new BrowsingContext[Documents];
        var documents = new Document[Documents];
        for (var i = 0; i < Documents; i++)
        {
            documents[i] = Load(html, out contexts[i]);

            // The style engine of the document (style sheets, rule indexes) is not part of the styles of the elements
            GC.KeepAlive(documents[i].DefaultView!.GetComputedStyle(documents[i].DocumentElement!).GetPropertyValue("display"));
        }

        var baseline = GetCompactedHeapSize();
        var length = 0;
        foreach (var document in documents)
        {
            length += ComputeStyles(document, allProperties);
        }

        var retained = (GetCompactedHeapSize() - baseline) / Documents;
        var elements = documents[0].QuerySelectorAll("*").Length;
        Console.WriteLine($"| {name} | {(allProperties ? "all" : string.Join(", ", Properties))} | {elements:N0} | {retained / (double)elements:N0} | {FormatRareData(documents[0])} |");
        GC.KeepAlive(length);
        foreach (var context in contexts)
        {
            context.Dispose();
        }
    }

    private static Document Load(string html, out BrowsingContext context)
    {
        context = new BrowsingContext();
        return context.Navigate("https://example.com/", _ => html);
    }

    private static int ComputeStyles(Document document, bool allProperties)
    {
        var window = document.DefaultView!;
        var length = 0;
        foreach (var element in document.QuerySelectorAll("*").Cast<Element>())
        {
            var style = window.GetComputedStyle(element);
            if (allProperties)
            {
                for (var i = 0u; i < style.Length; i++)
                {
                    length += style.GetPropertyValue(style.Item(i)).Length;
                }
            }
            else
            {
                foreach (var property in Properties)
                {
                    length += style.GetPropertyValue(property).Length;
                }
            }
        }

        return length;
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

    /// <summary>The size of an object of the type (the bytes allocated by an uninitialized instance).</summary>
    private static long GetInstanceSize(Type type)
    {
        GC.KeepAlive(RuntimeHelpers.GetUninitializedObject(type));
        var before = GC.GetAllocatedBytesForCurrentThread();
        var instance = RuntimeHelpers.GetUninitializedObject(type);
        var size = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(instance);
        return size;
    }

    private static readonly PropertyInfo? RareDataProperty = typeof(Node).GetProperty("RareDataOrNull", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>The number of elements of the document that have rare data, and the percentage of the elements.</summary>
    private static string FormatRareData(Document document)
    {
        if (RareDataProperty is not { } rareDataProperty)
            return "";

        var elements = 0;
        var withRareData = 0;
        Visit(document);
        return $"{withRareData:N0} ({withRareData * 100.0 / elements:N1}%)";

        void Visit(Node node)
        {
            if (node is Element)
            {
                elements++;
                if (rareDataProperty.GetValue(node) is not null)
                {
                    withRareData++;
                }
            }

            for (var child = node.FirstChild; child is not null; child = child.NextSibling)
            {
                Visit(child);
            }

            if (node is HtmlTemplateElement template)
            {
                Visit(template.Content);
            }
        }
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
}
