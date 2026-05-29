using Meziantou.Framework.InlineSnapshotTesting;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meziantou.AspNetCore.Components.LogViewer.Tests;

public sealed class LogViewerTests
{
    private static LogEntry Entry(string message, bool expanded = false, object? data = null, IReadOnlyList<LogEntry>? children = null)
        => new() { Message = message, LogLevel = LogLevel.Information, Timestamp = DateTimeOffset.UnixEpoch, Expanded = expanded, Data = data, Children = children };

    [Fact]
    public async Task FlatEntries_HaveSequentialLineNumbers()
    {
        var html = await RenderAsync([Entry("a"), Entry("b"), Entry("c")]);

        InlineSnapshot.Validate(html, """
            <div class="log-viewer" mez-logviewer>
                    <div class="log-entry " mez-logviewer><a class="log-linenumber" __internal_preventDefault_onclick mez-logviewer>1</a><span class="log-content" style="--log-depth: 0" mez-logviewer><span class="log-message log-information" mez-logviewer>a</span></span></div>
                    <div class="log-entry " mez-logviewer><a class="log-linenumber" __internal_preventDefault_onclick mez-logviewer>2</a><span class="log-content" style="--log-depth: 0" mez-logviewer><span class="log-message log-information" mez-logviewer>b</span></span></div>
                    <div class="log-entry " mez-logviewer><a class="log-linenumber" __internal_preventDefault_onclick mez-logviewer>3</a><span class="log-content" style="--log-depth: 0" mez-logviewer><span class="log-message log-information" mez-logviewer>c</span></span></div></div>
            """);
    }

    [Fact]
    public async Task NestedEntries_ExpandedByDefault_RenderChildrenIndented()
    {
        var html = await RenderAsync(
        [
            Entry("parent", expanded: true, children:
            [
                Entry("child 1"),
                Entry("child 2"),
            ]),
            Entry("sibling"),
        ]);

        InlineSnapshot.Validate(html, """
            <div class="log-viewer" mez-logviewer>
                    <div class="log-entry " mez-logviewer><a class="log-linenumber" __internal_preventDefault_onclick mez-logviewer>1</a><span class="log-content" style="--log-depth: 0" mez-logviewer><span class="log-toggle-details opened" mez-logviewer>▶</span><span class="log-message log-information" mez-logviewer>parent</span></span></div>
                    <div class="log-entry " mez-logviewer><a class="log-linenumber" __internal_preventDefault_onclick mez-logviewer>2</a><span class="log-content" style="--log-depth: 1" mez-logviewer><span class="log-message log-information" mez-logviewer>child 1</span></span></div>
                    <div class="log-entry " mez-logviewer><a class="log-linenumber" __internal_preventDefault_onclick mez-logviewer>3</a><span class="log-content" style="--log-depth: 1" mez-logviewer><span class="log-message log-information" mez-logviewer>child 2</span></span></div>
                    <div class="log-entry " mez-logviewer><a class="log-linenumber" __internal_preventDefault_onclick mez-logviewer>4</a><span class="log-content" style="--log-depth: 0" mez-logviewer><span class="log-message log-information" mez-logviewer>sibling</span></span></div></div>
            """);
    }

    [Fact]
    public async Task NestedEntries_CollapsedByDefault_HideChildrenButStillCountThemInLineNumbers()
    {
        // parent (1) has two collapsed children (2, 3) that are not rendered,
        // so the sibling keeps line number 4 even though only two rows are visible.
        var html = await RenderAsync(
        [
            Entry("parent", expanded: false, children:
            [
                Entry("child 1"),
                Entry("child 2"),
            ]),
            Entry("sibling"),
        ]);

        InlineSnapshot.Validate(html, """
            <div class="log-viewer" mez-logviewer>
                    <div class="log-entry " mez-logviewer><a class="log-linenumber" __internal_preventDefault_onclick mez-logviewer>1</a><span class="log-content" style="--log-depth: 0" mez-logviewer><span class="log-toggle-details " mez-logviewer>▶</span><span class="log-message log-information" mez-logviewer>parent</span></span></div>
                    <div class="log-entry " mez-logviewer><a class="log-linenumber" __internal_preventDefault_onclick mez-logviewer>4</a><span class="log-content" style="--log-depth: 0" mez-logviewer><span class="log-message log-information" mez-logviewer>sibling</span></span></div></div>
            """);
    }

    [Fact]
    public async Task DeepNesting_IncreasesDepthAtEachLevel()
    {
        var html = await RenderAsync(
        [
            Entry("level 0", expanded: true, children:
            [
                Entry("level 1", expanded: true, children:
                [
                    Entry("level 2", expanded: true, children:
                    [
                        Entry("level 3"),
                    ]),
                ]),
            ]),
        ]);

        InlineSnapshot.Validate(html, """
            <div class="log-viewer" mez-logviewer>
                    <div class="log-entry " mez-logviewer><a class="log-linenumber" __internal_preventDefault_onclick mez-logviewer>1</a><span class="log-content" style="--log-depth: 0" mez-logviewer><span class="log-toggle-details opened" mez-logviewer>▶</span><span class="log-message log-information" mez-logviewer>level 0</span></span></div>
                    <div class="log-entry " mez-logviewer><a class="log-linenumber" __internal_preventDefault_onclick mez-logviewer>2</a><span class="log-content" style="--log-depth: 1" mez-logviewer><span class="log-toggle-details opened" mez-logviewer>▶</span><span class="log-message log-information" mez-logviewer>level 1</span></span></div>
                    <div class="log-entry " mez-logviewer><a class="log-linenumber" __internal_preventDefault_onclick mez-logviewer>3</a><span class="log-content" style="--log-depth: 2" mez-logviewer><span class="log-toggle-details opened" mez-logviewer>▶</span><span class="log-message log-information" mez-logviewer>level 2</span></span></div>
                    <div class="log-entry " mez-logviewer><a class="log-linenumber" __internal_preventDefault_onclick mez-logviewer>4</a><span class="log-content" style="--log-depth: 3" mez-logviewer><span class="log-message log-information" mez-logviewer>level 3</span></span></div></div>
            """);
    }

    [Fact]
    public async Task ExpandedEntry_WithChildrenAndData_ShowsBothUnderASingleToggle()
    {
        var html = await RenderAsync(
        [
            Entry("parent", expanded: true, data: new { Key = "value" }, children:
            [
                Entry("child"),
            ]),
        ]);

        InlineSnapshot.Validate(html, """
            <div class="log-viewer" mez-logviewer>
                    <div class="log-entry " mez-logviewer><a class="log-linenumber" __internal_preventDefault_onclick mez-logviewer>1</a><span class="log-content" style="--log-depth: 0" mez-logviewer><span class="log-toggle-details opened" mez-logviewer>▶</span><span class="log-message log-information" mez-logviewer>parent</span></span></div>
                    <div class="log-entry " mez-logviewer><a class="log-linenumber" __internal_preventDefault_onclick mez-logviewer>2</a><span class="log-content" style="--log-depth: 1" mez-logviewer><span class="log-message log-information" mez-logviewer>child</span></span></div><div class="log-details" style="--log-depth: 0" mez-logviewer><div class="format-selector-group" b-2eabwp2010><a class="format-selector selected" href="#" __internal_preventDefault_onclick b-2eabwp2010>table</a> |
            			<a class="format-selector " href="#" __internal_preventDefault_onclick b-2eabwp2010>json</a></div><table b-2eabwp2010><tr b-2eabwp2010><th b-2eabwp2010>Key:</th>
            					<td b-2eabwp2010>value</td></tr></table></div></div>
            """);
    }

    // The test namespace 'Meziantou.AspNetCore.Components.LogViewer.Tests' makes the name
    // 'Meziantou.AspNetCore.Components.LogViewer' ambiguous between the namespace and the
    // LogViewer component type, so the type is resolved via reflection instead.
    private static readonly Type LogViewerComponentType = typeof(LogEntry).Assembly.GetType("Meziantou.AspNetCore.Components.LogViewer", throwOnError: true)!;

    private static async Task<string> RenderAsync(IEnumerable<LogEntry> entries)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Entries"] = entries,
            ["TimestampDisplayFormat"] = TimestampDisplayFormat.Hidden,
            ["ShowLineNumbers"] = true,
        };

        using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(serviceProvider, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync(LogViewerComponentType, ParameterView.FromDictionary(parameters));
            return component.ToHtmlString();
        });
    }
}
