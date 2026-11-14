using Meziantou.AspNetCore.Components.Tests.TestComponents;
using Meziantou.AspNetCore.Testing;
using Meziantou.AspNetCore.Testing.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Meziantou.AspNetCore.Components.Tests;

public sealed class TimeZoneServiceTests
{
    [Fact]
    public async Task ReadsTheTimeZoneOfTheBrowser()
    {
        // The time zone comes from Intl.DateTimeFormat of the page; the offset at an instant comes from the time zone
        await using var host = InteractiveTestHosts.CreateWithJavaScript();
        host.Services.AddTimeZoneServices();
        var instant = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        var page = await host.RenderAsync<TimeZonePage>(parameters => parameters.Add(c => c.Instant, instant));

        await page.GetByRole("button", "Detect").ClickAsync();

        var timeZoneId = page.Find("#timezone").TextContent;
        Assert.NotEmpty(timeZoneId);
        Assert.NotEqual("(unknown)", timeZoneId);
        Assert.Equal(TimeZoneInfo.FindSystemTimeZoneById(timeZoneId!).GetUtcOffset(instant).ToString(), page.Find("#offset").TextContent);
        Assert.Empty(host.ScriptErrors);
    }
}
