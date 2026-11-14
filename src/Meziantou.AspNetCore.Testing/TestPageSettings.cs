using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.AspNetCore.Testing;

/// <summary>The settings of the browsing context of a <see cref="TestPage"/>.</summary>
public sealed class TestPageSettings
{
    /// <summary>Gets the size of the viewport, in CSS pixels. The default is 1280x720.</summary>
    public ViewportOptions Viewport { get; init; } = new() { Width = 1280, Height = 720 };

    /// <summary>Gets the loader of the documents and of their resources, or null to fetch nothing.</summary>
    public DocumentLoader? DocumentLoader { get; init; }

    /// <summary>Gets the cookies of the page, or null for no cookie. Pages that share a jar share their cookies.</summary>
    public CookieJar? CookieJar { get; init; }

    /// <summary>
    /// Gets the system clipboard of the page: the data of the copy and cut actions and of <c>navigator.clipboard</c>, or null
    /// for a clipboard of its own. Pages that share a clipboard share what they copy.
    /// </summary>
    public SystemClipboard? Clipboard { get; init; }

    /// <summary>Gets the engine that runs the scripts of the page, or null to run no script. The page disposes it.</summary>
    public ScriptEngine? ScriptEngine { get; init; }

    /// <summary>Gets the clock of the timers of the page (<c>setTimeout()</c>, the animations...), or null for the system clock.</summary>
    public TimeProvider? TimeProvider { get; init; }

    /// <summary>Gets the maximum time the waits of the page wait for. The default is 10 seconds.</summary>
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets the name of the attribute that <see cref="LocatorScope.GetByTestId(string)"/> matches. The default is <c>data-testid</c>.</summary>
    public string TestIdAttribute { get; init; } = "data-testid";
}
