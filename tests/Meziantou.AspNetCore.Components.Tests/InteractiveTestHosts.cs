using Meziantou.AspNetCore.Testing.Components;

namespace Meziantou.AspNetCore.Components.Tests;

/// <summary>The test hosts of the interactive tests.</summary>
internal static class InteractiveTestHosts
{
    /// <summary>
    /// Creates a host whose page runs JavaScript: the components import their own modules, served at the URLs of a Razor
    /// class library (<c>_content/Meziantou.AspNetCore.Components/...</c>) from the output folder of the tests.
    /// </summary>
    public static ComponentTestHost CreateWithJavaScript() => new(new ComponentTestHostOptions
    {
        EnableJavaScript = true,
        StaticFiles = { ["/"] = Path.Combine(AppContext.BaseDirectory, "wwwroot") },
    });
}
