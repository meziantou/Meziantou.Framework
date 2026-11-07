using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// An event handler content attribute to compile (https://html.spec.whatwg.org/multipage/webappapis.html#getting-the-current-value-of-the-event-handler):
/// the body of a function whose parameters are <see cref="Parameters"/>, whose scope chain is the global environment of
/// the realm of <see cref="Window"/>, then <see cref="Document"/>, <see cref="Form"/> and <see cref="Element"/> (when they are
/// not null), and whose <c>this</c> value is the current target of the event.
/// </summary>
public sealed class EventHandlerSource
{
    internal EventHandlerSource(Window window, EventTarget target, Element? element, Document? document, HtmlFormElement? form, string name, string body, string url, IReadOnlyList<string> parameters)
    {
        Window = window;
        Target = target;
        Element = element;
        Document = document;
        Form = form;
        Name = name;
        Body = body;
        Url = url;
        Parameters = parameters;
    }

    /// <summary>Gets the global object of the realm in which the function is created.</summary>
    public Window Window { get; }

    /// <summary>Gets the target of the event handler: the element, or the window for the Window-reflecting body element event handlers (e.g. <c>&lt;body onload&gt;</c>).</summary>
    public EventTarget Target { get; }

    /// <summary>Gets the element added to the scope chain (the element of the content attribute), or null when <see cref="Target"/> is a window.</summary>
    public Element? Element { get; }

    /// <summary>Gets the document added to the scope chain (the node document of <see cref="Element"/>), or null when <see cref="Target"/> is a window.</summary>
    public Document? Document { get; }

    /// <summary>Gets the form owner of <see cref="Element"/> added to the scope chain, or null.</summary>
    public HtmlFormElement? Form { get; }

    /// <summary>Gets the name of the content attribute and of the function (e.g. <c>onclick</c>).</summary>
    public string Name { get; }

    /// <summary>Gets the body of the function: the value of the content attribute.</summary>
    public string Body { get; }

    /// <summary>Gets the URL of the document of the content attribute, used as the file name of the errors.</summary>
    public string Url { get; }

    /// <summary>
    /// Gets the parameters of the function: <c>event</c>, or <c>event</c>, <c>source</c>, <c>lineno</c>, <c>colno</c> and
    /// <c>error</c> for the <c>onerror</c> event handler of a window (an <c>OnErrorEventHandler</c>).
    /// </summary>
    public IReadOnlyList<string> Parameters { get; }
}
