using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.WebAssembly;

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


    /// <summary>Gets the engine that runs the scripts of the page, or null to run no script. The page disposes it.</summary>
    public ScriptEngine? ScriptEngine { get; init; }

    /// <summary>
    /// Gets the runtime that compiles and runs the WebAssembly modules of the scripts of the page, or null: the
    /// <c>WebAssembly</c> namespace is then not defined (see <see cref="BrowsingContextOptions.WebAssemblyRuntime"/>). The
    /// <c>Meziantou.Framework.WebPlatform.Wasm</c> package provides a runtime. The page does not dispose it.
    /// </summary>
    public WebAssemblyRuntime? WebAssemblyRuntime { get; init; }

    /// <summary>
    /// Gets the clock of the timers of the page (<c>setTimeout()</c>, the animations...), or null for the system clock. The
    /// clock of the dates of the scripts (<c>Date</c>) is the one of <see cref="ScriptEngine"/>: give the same clock to
    /// the engine (for instance <c>JavaScriptScriptEngineOptions.TimeProvider</c>) for the page to have one clock.
    /// </summary>
    public TimeProvider? TimeProvider { get; init; }

    /// <summary>Gets the maximum time the waits of the page wait for. The default is 10 seconds.</summary>
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets the name of the attribute that <see cref="LocatorScope.GetByTestId(string)"/> matches. The default is <c>data-testid</c>.</summary>
    public string TestIdAttribute { get; init; } = "data-testid";

    /// <summary>
    /// Gets a function that changes the options of the browsing context of the page, or null. It receives the options made
    /// from the other settings and returns the options to use, usually with a <c>with</c> expression:
    /// <c>options =&gt; options with { UserAgent = "..." }</c>. The page still records the console messages, the script
    /// errors and the content security policy violations, and calls the callbacks that the function sets. The function
    /// cannot replace <see cref="BrowsingContextOptions.DocumentLoader"/> nor <see cref="BrowsingContextOptions.ScriptEngine"/>:
    /// the page waits for the requests of its loader and calls the functions of its engine, so set <see cref="DocumentLoader"/>
    /// and <see cref="ScriptEngine"/> instead.
    /// </summary>
    /// <remarks>The page throws an <see cref="InvalidOperationException"/> when the function replaces them.</remarks>
    public Func<BrowsingContextOptions, BrowsingContextOptions>? ConfigureBrowsingContext { get; init; }

    /// <summary>
    /// Gets the controller that answers the dialogs of the page and records them, or null for a controller of the page in
    /// <see cref="DialogMode.Strict"/> mode (see <see cref="TestPage.Dialogs"/>).
    /// </summary>
    public DialogController? Dialogs { get; init; }
}
