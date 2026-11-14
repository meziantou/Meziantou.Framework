using Meziantou.AspNetCore.Components.Tests.TestComponents;
using Meziantou.AspNetCore.Testing.Components;
using Meziantou.Framework.InlineSnapshotTesting;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meziantou.AspNetCore.Components.Tests;

public sealed class RepeaterTests
{
    [Fact]
    public async Task NullDataShowLoadingIndicator()
    {
        var html = await RenderRepeaterAsync(
            items: null,
            loadingTemplate: MarkupTemplate("<p>Loading...</p>"),
            emptyTemplate: MarkupTemplate("<p>empty</p>"));

        InlineSnapshot.Validate(html, """<p>Loading...</p>""");
    }

    [Fact]
    public async Task EmptyTemplate()
    {
        var html = await RenderRepeaterAsync(
            items: [],
            loadingTemplate: MarkupTemplate("<p>loading...</p>"),
            emptyTemplate: MarkupTemplate("<p>empty</p>"));

        InlineSnapshot.Validate(html, """<p>empty</p>""");
    }

    [Fact]
    public async Task ItemTemplate()
    {
        var html = await RenderRepeaterAsync(
            items: ["a", "b"],
            emptyTemplate: MarkupTemplate("<p>empty</p>"),
            itemTemplate: item => TextTemplate(item));

        InlineSnapshot.Validate(html, """ab""");
    }

    [Fact]
    public async Task ItemTemplateAndSeparatorTemplate()
    {
        var html = await RenderRepeaterAsync(
            items: ["a", "b"],
            emptyTemplate: MarkupTemplate("<p>empty</p>"),
            itemTemplate: item => TextTemplate(item),
            itemSeparatorTemplate: TextTemplate(","));

        InlineSnapshot.Validate(html, """a,b""");
    }

    [Fact]
    public async Task ContainerTemplate()
    {
        var html = await RenderRepeaterAsync(
            items: ["a", "b"],
            emptyTemplate: MarkupTemplate("<p>empty</p>"),
            itemTemplate: item => TextTemplate(item),
            repeaterContainerTemplate: itemsTemplate => builder =>
            {
                builder.OpenElement(0, "div");
                builder.AddContent(1, itemsTemplate);
                builder.CloseElement();
            });

        InlineSnapshot.Validate(html, """<div>ab</div>""");
    }

    private static RenderFragment MarkupTemplate(string html)
        => builder => builder.AddMarkupContent(0, html);

    private static RenderFragment TextTemplate(string text)
        => builder => builder.AddContent(0, text);

    private static async Task<string> RenderRepeaterAsync(
        IEnumerable<string>? items,
        RenderFragment? loadingTemplate = null,
        RenderFragment? emptyTemplate = null,
        RenderFragment<string>? itemTemplate = null,
        RenderFragment? itemSeparatorTemplate = null,
        RenderFragment<RenderFragment>? repeaterContainerTemplate = null)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [nameof(Repeater<string>.Items)] = items,
            [nameof(Repeater<string>.LoadingTemplate)] = loadingTemplate,
            [nameof(Repeater<string>.EmptyTemplate)] = emptyTemplate,
            [nameof(Repeater<string>.ItemTemplate)] = itemTemplate,
            [nameof(Repeater<string>.ItemSeparatorTemplate)] = itemSeparatorTemplate,
            [nameof(Repeater<string>.RepeaterContainerTemplate)] = repeaterContainerTemplate,
        };

        using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(serviceProvider, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<Repeater<string>>(ParameterView.FromDictionary(parameters));
            return component.ToHtmlString();
        });
    }

    [Fact]
    public async Task Interactive_SwitchesBetweenLoadingEmptyAndItems()
    {
        await using var host = new ComponentTestHost();
        var repeater = await host.RenderAsync<RepeaterPage>(parameters => parameters.Add(c => c.Items, null));
        Assert.Equal("Loading...", repeater.Find("p").TextContent);

        await repeater.SetParametersAsync(parameters => parameters.Add(c => c.Items, Array.Empty<string>()));
        Assert.Equal("No items", repeater.Find("p").TextContent);

        await repeater.SetParametersAsync(parameters => parameters.Add(c => c.Items, ["a", "b", "c"]));
        Assert.Equal(["a", "", "b", "", "c"], repeater.FindAll("ul > li").Select(item => item.TextContent));
        Assert.HasCount(2, repeater.FindAll("li[role=separator]"));

        // A lazy sequence is enumerated once per render
        var enumerations = 0;
        await repeater.SetParametersAsync(parameters => parameters.Add(c => c.Items, Enumerate()));
        Assert.Equal(1, enumerations);
        Assert.Equal(["x"], repeater.FindAll("ul > li").Select(item => item.TextContent));

        IEnumerable<string> Enumerate()
        {
            enumerations++;
            yield return "x";
        }
    }
}
