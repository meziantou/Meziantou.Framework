using Meziantou.AspNetCore.Testing.Components;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Formatting;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meziantou.AspNetCore.Testing.Fuzz.Components;

/// <summary>
/// The input is a program that edits a tree of elements, texts, markup blocks, child components and regions (insertions,
/// removals, moves of keyed and unkeyed nodes, attributes, texts), rendered by a component. After each render, the DOM
/// built by the test host from the render batches (the diffs) must be the DOM of the HTML that the static renderer of
/// Blazor (<see cref="HtmlRenderer"/>) produces for the same tree.
/// </summary>
public static class ComponentFuzzTargets
{
    private const int MaxOperations = 48;
    private const int MaxNodes = 80;

    private static readonly string[] HtmlTags = ["div", "span", "section", "em", "strong"];
    private static readonly string[] SvgTags = ["g", "circle", "rect"];
    private static readonly string[] AttributeNames = ["class", "id", "title", "data-x"];
    private static readonly string[] Values = ["", "a", "b c", "<&>\"'", "é", "x"];
    private static readonly string[] Markups = ["<b>bold</b>", "text &amp; more", "<i>a</i><u>b</u>", "", "<span class=\"m\">m</span> tail"];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("components-render-batches", RenderBatches) { Seeds = GetSeeds, Dictionary = ["\u0000\u0000\u0000", "\u0001\u0000", "\u0002\u0000\u0000\u0001", "\u0006\u0000", "\u0007"] },
    ];

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        // Insert keyed elements, a component, a region and markup, render, then move and remove them
        yield return new FuzzSeed("insertions", new byte[] { 0, 0, 0, 0x10, 0, 0, 1, 0x11, 0, 0, 2, 0x13, 0, 0, 3, 0x14, 0, 0, 4, 0x12, 7 });
        yield return new FuzzSeed("moves", new byte[] { 0, 0, 0, 0x10, 0, 0, 1, 0x10, 0, 0, 2, 0x10, 0, 0, 3, 0x13, 7, 6, 0, 7, 2, 0, 0, 3, 7, 1, 0, 1, 7 });
        yield return new FuzzSeed("attributes-and-texts", new byte[] { 0, 0, 0, 0x10, 0, 0, 1, 0x11, 7, 3, 0, 0, 1, 2, 7, 4, 0, 1, 3, 7, 5, 0, 0, 1, 7 });
        yield return new FuzzSeed("svg", new byte[] { 0, 0, 0, 0x15, 0, 1, 0, 0x10, 0, 1, 1, 0x11, 7, 6, 1, 7 });
    }

    private static void RenderBatches(ReadOnlySpan<byte> data)
    {
        var root = new ModelContainer(inSvg: false);
        var nextKey = 0;
        using var host = new ComponentTestHost(new ComponentTestHostOptions { DefaultTimeout = TimeSpan.FromSeconds(30) });
        var component = host.RenderAsync<ModelComponent>(parameters => parameters.Add(c => c.Nodes, root.Children)).GetAwaiter().GetResult();
        var rendered = false;
        for (var operation = 0; operation < MaxOperations && !data.IsEmpty; operation++)
        {
            var opCode = FuzzInput.ReadByte(ref data) % 8;
            var containers = new List<ModelContainer>();
            root.CollectContainers(containers);
            var container = FuzzInput.Pick(containers, FuzzInput.ReadByte(ref data));
            var children = container.Children;
            switch (opCode)
            {
                case 0 when CountNodes(root) < MaxNodes:
                    children.Insert(FuzzInput.ReadByte(ref data) % (children.Count + 1), CreateNode(container.InSvg, FuzzInput.ReadByte(ref data), ref nextKey));
                    break;

                case 1 when children.Count > 0:
                    children.RemoveAt(FuzzInput.ReadByte(ref data) % children.Count);
                    break;

                case 2 when children.Count > 0:
                    var from = FuzzInput.ReadByte(ref data) % children.Count;
                    var node = children[from];
                    children.RemoveAt(from);
                    children.Insert(FuzzInput.ReadByte(ref data) % (children.Count + 1), node);
                    break;

                case 3 when children.Count > 0:
                    if (children[FuzzInput.ReadByte(ref data) % children.Count] is ModelElement element)
                    {
                        var name = FuzzInput.Pick(AttributeNames, FuzzInput.ReadByte(ref data));
                        var value = FuzzInput.ReadByte(ref data);
                        if (value % (Values.Length + 1) == Values.Length)
                        {
                            element.Attributes.Remove(name);
                        }
                        else
                        {
                            element.Attributes[name] = FuzzInput.Pick(Values, value);
                        }
                    }

                    break;

                case 4 when children.Count > 0:
                    if (children[FuzzInput.ReadByte(ref data) % children.Count] is ModelText text)
                    {
                        text.Text = FuzzInput.Pick(Values, FuzzInput.ReadByte(ref data));
                    }

                    break;

                case 5 when children.Count > 0:
                    if (children[FuzzInput.ReadByte(ref data) % children.Count] is ModelMarkup markup)
                    {
                        markup.Html = FuzzInput.Pick(Markups, FuzzInput.ReadByte(ref data));
                    }

                    break;

                case 6:
                    children.Reverse();
                    break;

                case 7:
                    Render(host, component, root);
                    rendered = true;
                    continue;
            }

            rendered = false;
        }

        if (!rendered)
        {
            Render(host, component, root);
        }
    }

    private static void Render(ComponentTestHost host, RenderedComponent<ModelComponent> component, ModelContainer root)
    {
        host.InvokeAsync(component.Instance.Refresh).GetAwaiter().GetResult();
        var actual = component.GetMarkup(new MarkupOptions { Indent = false, IncludeFormState = false });

        // The HTML of the static renderer, parsed and serialized by the same HTML serializer as the DOM of the test host
        var html = RenderStatic(root);
        var template = (HtmlTemplateElement)host.Document.CreateElement("template");
        template.InnerHtml = html;
        var expected = HtmlFormatter.Default.Format(template.Content).Trim();
        FuzzAssert.Equal(expected, actual, "The DOM of the test host differs from the output of HtmlRenderer");
    }

    private static string RenderStatic(ModelContainer root)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        return renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<ModelComponent>(ParameterView.FromDictionary(new Dictionary<string, object?>(StringComparer.Ordinal) { [nameof(ModelComponent.Nodes)] = root.Children }));
            return output.ToHtmlString();
        }).GetAwaiter().GetResult();
    }

    private static ModelNode CreateNode(bool inSvg, byte kind, ref int nextKey)
    {
        var hasKey = (kind & 0x08) != 0;
        int? key = hasKey ? nextKey++ : null;
        switch (kind % 8)
        {
            case 0:
                return new ModelElement(FuzzInput.Pick(inSvg ? SvgTags : HtmlTags, (byte)(kind >> 4)), key, inSvg);
            case 1:
                return new ModelText(FuzzInput.Pick(Values, (byte)(kind >> 4)));
            case 2 when !inSvg:
                return new ModelMarkup(FuzzInput.Pick(Markups, (byte)(kind >> 4)));
            case 3:
                return new ModelComponentNode(key, inSvg);
            case 4:
                return new ModelRegion(inSvg);
            case 5 when !inSvg:
                return new ModelElement("svg", key, inSvg: true);
            default:
                return new ModelText("t");
        }
    }

    private static int CountNodes(ModelContainer container)
    {
        var count = 0;
        foreach (var child in container.Children)
        {
            count++;
            if (child is ModelContainer childContainer)
            {
                count += CountNodes(childContainer);
            }
        }

        return count;
    }
}
