namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The event handlers of <see cref="XmlHttpRequest"/> and <see cref="XmlHttpRequestUpload"/> (https://xhr.spec.whatwg.org/#xmlhttprequesteventtarget).</summary>
[IdlInterface("XMLHttpRequestEventTarget", "https://xhr.spec.whatwg.org/#xmlhttprequesteventtarget")]
public abstract class XmlHttpRequestEventTarget : EventTarget
{
    private protected XmlHttpRequestEventTarget()
    {
    }

    /// <summary>Gets or sets the event handler of the <c>loadstart</c> event.</summary>
    [IdlMember("onloadstart", "https://xhr.spec.whatwg.org/#handler-xhr-onloadstart")]
    public Action<Event>? OnLoadStart
    {
        get => GetEventHandler("loadstart");
        set => SetEventHandler("loadstart", value);
    }

    /// <summary>Gets or sets the event handler of the <c>progress</c> event.</summary>
    [IdlMember("onprogress", "https://xhr.spec.whatwg.org/#handler-xhr-onprogress")]
    public Action<Event>? OnProgress
    {
        get => GetEventHandler("progress");
        set => SetEventHandler("progress", value);
    }

    /// <summary>Gets or sets the event handler of the <c>abort</c> event.</summary>
    [IdlMember("onabort", "https://xhr.spec.whatwg.org/#handler-xhr-onabort")]
    public Action<Event>? OnAbort
    {
        get => GetEventHandler("abort");
        set => SetEventHandler("abort", value);
    }

    /// <summary>Gets or sets the event handler of the <c>error</c> event.</summary>
    [IdlMember("onerror", "https://xhr.spec.whatwg.org/#handler-xhr-onerror")]
    public Action<Event>? OnError
    {
        get => GetEventHandler("error");
        set => SetEventHandler("error", value);
    }

    /// <summary>Gets or sets the event handler of the <c>load</c> event.</summary>
    [IdlMember("onload", "https://xhr.spec.whatwg.org/#handler-xhr-onload")]
    public Action<Event>? OnLoad
    {
        get => GetEventHandler("load");
        set => SetEventHandler("load", value);
    }

    /// <summary>Gets or sets the event handler of the <c>timeout</c> event.</summary>
    [IdlMember("ontimeout", "https://xhr.spec.whatwg.org/#handler-xhr-ontimeout")]
    public Action<Event>? OnTimeout
    {
        get => GetEventHandler("timeout");
        set => SetEventHandler("timeout", value);
    }

    /// <summary>Gets or sets the event handler of the <c>loadend</c> event.</summary>
    [IdlMember("onloadend", "https://xhr.spec.whatwg.org/#handler-xhr-onloadend")]
    public Action<Event>? OnLoadEnd
    {
        get => GetEventHandler("loadend");
        set => SetEventHandler("loadend", value);
    }

    /// <summary>https://xhr.spec.whatwg.org/#concept-event-fire-progress</summary>
    internal void FireProgressEvent(string type, long transmitted, long length)
        => FireEvent(new ProgressEvent(type, new ProgressEventInit { LengthComputable = length != 0, Loaded = transmitted, Total = length }));
}

/// <summary>The upload of an <see cref="XmlHttpRequest"/>: the target of the progress events of the request body (https://xhr.spec.whatwg.org/#xmlhttprequestupload).</summary>
[IdlInterface("XMLHttpRequestUpload", "https://xhr.spec.whatwg.org/#xmlhttprequestupload")]
public sealed class XmlHttpRequestUpload : XmlHttpRequestEventTarget
{
    internal XmlHttpRequestUpload()
    {
    }

    /// <summary>Whether an event listener is registered (https://xhr.spec.whatwg.org/#upload-listener-flag).</summary>
    internal bool HasEventListeners => Array.Exists(EventListenerList, listener => !listener.Removed);
}

/// <summary>The type of the response of an <see cref="XmlHttpRequest"/> (https://xhr.spec.whatwg.org/#xmlhttprequestresponsetype).</summary>
[IdlInterface("XMLHttpRequestResponseType", "https://xhr.spec.whatwg.org/#xmlhttprequestresponsetype")]
public enum XmlHttpRequestResponseType
{
    /// <summary>The text of the response (the default).</summary>
    [IdlMember("", "https://xhr.spec.whatwg.org/#dom-xmlhttprequestresponsetype")]
    Default,

    /// <summary>The response is returned as an <c>ArrayBuffer</c>.</summary>
    [IdlMember("arraybuffer", "https://xhr.spec.whatwg.org/#dom-xmlhttprequestresponsetype-arraybuffer")]
    ArrayBuffer,

    /// <summary>The response is returned as a <c>Blob</c>.</summary>
    [IdlMember("blob", "https://xhr.spec.whatwg.org/#dom-xmlhttprequestresponsetype-blob")]
    Blob,

    /// <summary>The response is returned as a parsed <c>Document</c>.</summary>
    [IdlMember("document", "https://xhr.spec.whatwg.org/#dom-xmlhttprequestresponsetype-document")]
    Document,

    /// <summary>The response is returned as a value parsed from JSON.</summary>
    [IdlMember("json", "https://xhr.spec.whatwg.org/#dom-xmlhttprequestresponsetype-json")]
    Json,

    /// <summary>The response is returned as a string.</summary>
    [IdlMember("text", "https://xhr.spec.whatwg.org/#dom-xmlhttprequestresponsetype-text")]
    Text,
}
