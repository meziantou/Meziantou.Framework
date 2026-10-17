using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

// Cross-document messaging (https://html.spec.whatwg.org/multipage/web-messaging.html#web-messaging)
public sealed partial class Window
{
    /// <summary>
    /// Posts a message to the window (https://html.spec.whatwg.org/multipage/web-messaging.html#dom-window-postmessage): a
    /// <c>message</c> event is fired at the window by a task, when its document has the origin <paramref name="targetOrigin"/>.
    /// </summary>
    /// <param name="message">The message: a value of the script engine, a .NET primitive value or a serializable platform object, or null for <c>undefined</c>.</param>
    /// <param name="targetOrigin">The origin the document of the window must have: an absolute URL (only its origin is used), <c>*</c> (any origin) or <c>/</c> (the origin of the caller).</param>
    /// <param name="transfer">The objects to transfer (the <c>ArrayBuffer</c> objects of the script engine, and the message ports).</param>
    /// <exception cref="DomException">A <c>SyntaxError</c> when <paramref name="targetOrigin"/> is not a valid URL, or a <c>DataCloneError</c> when the message cannot be serialized.</exception>
    [IdlMember("postMessage", "https://html.spec.whatwg.org/multipage/web-messaging.html#dom-window-postmessage")]
    public void PostMessage(object? message, string targetOrigin, IReadOnlyList<object>? transfer = null)
    {
        ArgumentNullException.ThrowIfNull(targetOrigin);
        PostMessage(message, new WindowPostMessageOptions { TargetOrigin = targetOrigin, Transfer = transfer ?? [] });
    }

    /// <summary>
    /// Posts a message to the window (https://html.spec.whatwg.org/multipage/web-messaging.html#dom-window-postmessage-options):
    /// runs the window post message steps.
    /// </summary>
    /// <param name="message">The message: a value of the script engine, a .NET primitive value or a serializable platform object, or null for <c>undefined</c>.</param>
    /// <param name="options">The target origin (<c>/</c> by default) and the objects to transfer.</param>
    /// <exception cref="DomException">A <c>SyntaxError</c> when the target origin is not a valid URL, or a <c>DataCloneError</c> when the message cannot be serialized.</exception>
    [IdlMember("postMessage", "https://html.spec.whatwg.org/multipage/web-messaging.html#dom-window-postmessage-options")]
    public void PostMessage(object? message, WindowPostMessageOptions? options = null)
    {
        // https://html.spec.whatwg.org/multipage/web-messaging.html#window-post-message-steps
        // The incumbent settings object is approximated by the realm of the caller (the current realm when the method is
        // called by a script), else the window itself (a call from .NET)
        var incumbent = Options.ScriptEngine?.GetCurrentGlobal() ?? GlobalScope.Current ?? this;
        var incumbentOrigin = incumbent.OriginRecord;
        var targetOrigin = options?.TargetOrigin ?? "/";
        OriginRecord? targetOriginRecord = null;
        if (targetOrigin == "/")
        {
            targetOriginRecord = incumbentOrigin;
        }
        else if (targetOrigin != "*")
        {
            var parsedUrl = UrlParser.Parse(targetOrigin, baseUrl: null) ?? throw DomException.Syntax($"The target origin '{targetOrigin}' is not a valid URL");
            targetOriginRecord = OriginRecord.FromUrl(parsedUrl);
        }

        // The message is serialized in the current realm: the realm of the method, i.e. of the window when the caller can
        // access it (its exceptions are created there), else the realm of the caller (the functions of the cross-origin
        // properties are created in the realm of the caller). The windows that can reach each other share the script
        // engine: the message is serialized for the realms of the engine (e.g. a SharedArrayBuffer is shared, and the agent
        // cluster of the target is checked when it is deserialized).
        GlobalScope current = incumbent is Window incumbentWindow && Document.IsSameOriginDomain(incumbentWindow.Document, Document) ? this : incumbent;
        var serialized = PortMessage.Serialize(current, message, options?.Transfer ?? [], sameAgent: ReferenceEquals(current.ScriptEngine, ScriptEngine));
        var source = incumbent as Window;

        // The task is queued on the posted message task source of the window (https://html.spec.whatwg.org/multipage/webappapis.html#queue-a-global-task),
        // and runs when the document of the window is fully active. Unlike the specification, which ties the task to the
        // document of the window when the message is posted, a message posted to an initial about:blank document whose
        // window is reused by the next document (e.g. an iframe whose javascript: URL or srcdoc document is not loaded
        // yet) is delivered to that document, as in the browsers, which replace these documents sooner.
        EventLoop.QueueTask(() =>
        {
            if (!Document.IsFullyActive || (targetOriginRecord is not null && !OriginRecord.IsSameOrigin(Document.Origin, targetOriginRecord)))
                return;

            if (!serialized.TryDeserialize(this, out var data, out var ports))
            {
                FireEvent(new MessageEvent("messageerror", new MessageEventInit { Source = source }) { OriginRecord = incumbentOrigin });
                return;
            }

            FireEvent(new MessageEvent("message", new MessageEventInit { Data = data, Ports = ports, Source = source }) { OriginRecord = incumbentOrigin });
        });
    }
}
