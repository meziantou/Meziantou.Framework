using Meziantou.AspNetCore.Components.Tests.TestComponents;
using Meziantou.AspNetCore.Testing.Components;

namespace Meziantou.AspNetCore.Components.Tests;

public sealed class InfiniteScrollingTests
{
    [Fact]
    public async Task LoadsTheNextPagesWhenTheUserScrolls()
    {
        // The component imports its module, observes its last element with an IntersectionObserver, and the observer
        // calls LoadMoreItems on a DotNetObjectReference: everything runs in the page
        await using var host = InteractiveTestHosts.CreateWithJavaScript();
        var requests = new List<int>();
        var page = await host.RenderAsync<InfiniteScrollingPage>(parameters => parameters.Add(c => c.ItemsProvider, request =>
        {
            requests.Add(request.StartIndex);
            var count = request.StartIndex < 15 ? 5 : 0;
            return Task.FromResult(Enumerable.Range(request.StartIndex, count));
        }));

        // The first page fills the scroll container (5 items of 40px in 100px): the last element is not visible anymore
        await host.WaitForAssertionAsync(() => Assert.HasCount(5, page.FindAll(".item")));
        Assert.Equal([0], requests);

        var scroller = page.Find("#scroller");
        for (var expected = 10; expected <= 15; expected += 5)
        {
            await host.Input.WheelAsync(scroller, 0, 1000);
            await host.WaitForAssertionAsync(() => Assert.HasCount(expected, page.FindAll(".item")));
        }

        // The provider returns no item: the enumeration is completed
        await host.Input.WheelAsync(scroller, 0, 1000);
        await host.WaitForAssertionAsync(() => Assert.Equal([0, 5, 10, 15], requests));
        Assert.Equal("Item 14", page.FindAll(".item")[^1].TextContent);
        Assert.Empty(host.ScriptErrors);
    }
}
