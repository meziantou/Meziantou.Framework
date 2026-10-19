using BenchmarkDotNet.Attributes;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Benchmarks;

/// <summary>Builds a tree with the DOM API: creates elements, sets attributes and text, appends and removes nodes.</summary>
[MemoryDiagnoser]
public class DomBenchmarks
{
    private const int Count = 1000;

    [Benchmark]
    public int Meziantou()
    {
        var document = HtmlDocumentParser.ParseDocument("<ul></ul>");
        var list = document.QuerySelector("ul")!;
        for (var i = 0; i < Count; i++)
        {
            var item = document.CreateElement("li");
            item.SetAttribute("class", "item");
            item.SetAttribute("data-index", "1");
            item.TextContent = "Item";
            list.AppendChild(item);
        }

        for (var child = list.FirstChild; child is not null;)
        {
            var next = child.NextSibling?.NextSibling;
            list.RemoveChild(child);
            child = next;
        }

        return (int)list.ChildNodes.Length;
    }
}
