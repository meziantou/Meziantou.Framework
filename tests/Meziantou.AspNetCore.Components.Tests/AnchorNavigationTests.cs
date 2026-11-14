using Meziantou.AspNetCore.Components.Tests.TestComponents;
using Meziantou.AspNetCore.Testing.Components;

namespace Meziantou.AspNetCore.Components.Tests;

public sealed class AnchorNavigationTests
{
    [Fact]
    public async Task ScrollsToTheElementOfTheFragment()
    {
        await using var host = InteractiveTestHosts.CreateWithJavaScript();
        var page = await host.RenderAsync<AnchorPage>();
        Assert.Equal(0, host.BrowsingContext.Window.ScrollY);

        // The module scrolls the element into view, smoothly
        await host.NavigateAsync("#target");
        await host.WaitForAssertionAsync(() => Assert.Equal(0, page.Find("#target").GetBoundingClientRect().Top, 1.0));
        Assert.Empty(host.ScriptErrors);
    }

    [Fact]
    public async Task ScrollsToTheFragmentOfTheFirstUrl()
    {
        await using var host = InteractiveTestHosts.CreateWithJavaScript();
        await host.NavigateAsync("#target:~:text=Target");
        var page = await host.RenderAsync<AnchorPage>();

        await host.WaitForAssertionAsync(() => Assert.Equal(0, page.Find("#target").GetBoundingClientRect().Top, 1.0));
    }
}
