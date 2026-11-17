using Meziantou.AspNetCore.Components.Tests.TestComponents;
using Meziantou.AspNetCore.Testing;
using Meziantou.AspNetCore.Testing.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Meziantou.AspNetCore.Components.Tests;

public sealed class ClipboardServiceTests
{
    [Fact]
    public async Task WritesAndReadsTheClipboardOfThePage()
    {
        // navigator.clipboard of the page, after a click (the user activation of the clipboard API)
        await using var host = InteractiveTestHosts.CreateWithJavaScript();
        host.Services.AddClipboard();
        var page = await host.RenderAsync<ClipboardPage>();

        await page.Find("#text").FillAsync("Hello clipboard");
        await page.GetByRole("button", "Copy").ClickAsync();
        await page.GetByRole("button", "Paste").ClickAsync();

        await host.WaitForAssertionAsync(() => Assert.Equal("Hello clipboard", page.Find("#pasted").TextContent));
        Assert.Empty(host.ScriptErrors);
    }

    [Fact]
    public async Task CallsTheClipboardApi()
    {
        // Without JavaScript, the calls are set up and recorded
        await using var host = new ComponentTestHost();
        host.Services.AddClipboard();
        host.JSInterop.SetupVoid("navigator.clipboard.writeText");
        host.JSInterop.Setup("navigator.clipboard.readText", "from the clipboard");
        var page = await host.RenderAsync<ClipboardPage>();

        await page.Find("#text").FillAsync("Hello");
        await page.GetByRole("button", "Copy").ClickAsync();
        await page.GetByRole("button", "Paste").ClickAsync();

        Assert.Equal(["Hello"], host.JSInterop.GetInvocations("navigator.clipboard.writeText").Select(invocation => invocation.Arguments[0]));
        Assert.Equal("from the clipboard", page.Find("#pasted").TextContent);
    }
}
