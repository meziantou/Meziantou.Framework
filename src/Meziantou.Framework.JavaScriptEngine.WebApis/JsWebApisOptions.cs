namespace Meziantou.Framework.JavaScriptEngine.WebApis;

/// <summary>The web APIs that <see cref="JsWebApis.Install"/> adds to the global object of a realm. They are all enabled by default.</summary>
public sealed class JsWebApisOptions
{
    /// <summary>Gets or sets a value indicating whether the <c>console</c> object is added.</summary>
    public bool Console { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether <c>setTimeout</c>, <c>setInterval</c>, <c>clearTimeout</c> and <c>clearInterval</c> are added.</summary>
    public bool Timers { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether <c>queueMicrotask</c> is added.</summary>
    public bool QueueMicrotask { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether <c>structuredClone</c> is added.</summary>
    public bool StructuredClone { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the <c>URL</c> and <c>URLSearchParams</c> classes are added.</summary>
    public bool Url { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the <c>TextEncoder</c> and <c>TextDecoder</c> classes are added.</summary>
    public bool Encoding { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether <c>atob</c> and <c>btoa</c> are added.</summary>
    public bool Base64 { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the <c>DOMException</c> class is added. The other APIs throw
    /// <c>DOMException</c> objects (e.g. <c>atob</c>) even when the class is not added.
    /// </summary>
    public bool DomException { get; set; } = true;

    /// <summary>
    /// Gets or sets the handler of the messages of the <c>console</c> object. When it is null, the messages are written to
    /// <see cref="System.Console.Out"/>, and the warnings and errors to <see cref="System.Console.Error"/>.
    /// </summary>
    public Action<JsConsoleMessage>? ConsoleMessage { get; set; }
}
