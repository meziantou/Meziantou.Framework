using Meziantou.AspNetCore.Components.Tests.TestComponents;
using Meziantou.AspNetCore.Testing;
using Meziantou.AspNetCore.Testing.Components;
using static Meziantou.AspNetCore.Testing.Expectations;

namespace Meziantou.AspNetCore.Components.Tests;

public sealed class LoadingIndicatorTests
{
    [Fact]
    public async Task ShowsTheLoadingTextWhileLoading()
    {
        await using var host = new ComponentTestHost();
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = await host.RenderAsync<LoadingPage>(parameters => parameters.Add(c => c.Load, () => completion.Task));
        Assert.Equal("", page.Find("#result").TextContent);

        // The click does not wait for the event handler, which waits for the completion
        await page.GetByRole("button", "Load").ClickAsync(new ClickOptions { NoWaitAfter = true });
        Assert.Null(page.FindOrDefault("#result"));
        await Expect(page.GetByText("Please wait")).ToBeVisibleAsync();

        // The assertions wait until the component renders the result
        completion.SetResult("Loaded");
        await Expect(page.Locator("#result")).ToHaveTextAsync("Loaded");
        await Expect(page.GetByText("Please wait")).ToBeHiddenAsync();
    }
}
