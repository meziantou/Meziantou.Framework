using System.Text;
using BenchmarkDotNet.Attributes;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Benchmarks;

/// <summary>
/// The accessibility properties of all the elements of a table, as a role locator computes them (Meziantou.AspNetCore.Testing
/// GetByRole): the cost of each element must not grow with the size of the document.
/// </summary>
[MemoryDiagnoser]
public class AccessibilityBenchmarks
{
    private Element[] _elements = [];

    [Params(200)]
    public int Rows { get; set; }

    /// <summary>Whether an element of the document has an aria-owns attribute.</summary>
    [Params(false, true)]
    public bool AriaOwns { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html><body><h1>Items</h1>");
        if (AriaOwns)
        {
            html.Append("<div role='list' aria-owns='owned'></div><div id='owned' role='listitem'>Owned</div>");
        }

        html.Append("<table><thead><tr><th>Name</th><th>Quantity</th><th>Actions</th></tr></thead><tbody>");
        for (var i = 0; i < Rows; i++)
        {
            html.Append("<tr><td><a href='/items/").Append(i).Append("'>Item ").Append(i).Append("</a></td><td><input type='number' aria-label='Quantity ").Append(i)
                .Append("' value='1'></td><td><button type='button'>Edit</button> <button type='button' aria-label='Delete item ").Append(i).Append("'><span>X</span></button></td></tr>");
        }

        html.Append("</tbody></table><button type='button'>Save</button></body></html>");

        var document = HtmlDocumentParser.ParseDocument(html.ToString());
        _elements = [.. document.QuerySelectorAll("*").Cast<Element>()];
    }

    [Benchmark]
    public int GetByRole()
    {
        var count = 0;
        foreach (var element in _elements)
        {
            var properties = element.GetAccessibilityProperties();
            if (properties.Role == "button" && properties.Name == "Save")
            {
                count++;
            }
        }

        return count;
    }
}
