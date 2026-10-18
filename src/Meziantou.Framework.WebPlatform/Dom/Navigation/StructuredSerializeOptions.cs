namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://html.spec.whatwg.org/multipage/structured-data.html#structuredserializeoptions</summary>
[IdlInterface("StructuredSerializeOptions", "https://html.spec.whatwg.org/multipage/structured-data.html#structuredserializeoptions")]
public class StructuredSerializeOptions
{
    /// <summary>Gets the objects to transfer (the <c>ArrayBuffer</c> objects of the script engine, and the message ports).</summary>
    [IdlMember("transfer", "https://html.spec.whatwg.org/multipage/structured-data.html#dom-structuredserializeoptions-transfer")]
    public IReadOnlyList<object> Transfer { get; init; } = [];
}

/// <summary>The options of <see cref="Window.PostMessage(object?, WindowPostMessageOptions?)"/> (https://html.spec.whatwg.org/multipage/nav-history-apis.html#windowpostmessageoptions).</summary>
[IdlInterface("WindowPostMessageOptions", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#windowpostmessageoptions")]
public sealed class WindowPostMessageOptions : StructuredSerializeOptions
{
    /// <summary>
    /// Gets the origin the target window must have for the message to be delivered: an absolute URL (only its origin is
    /// used), <c>*</c> (any origin) or <c>/</c> (the origin of the caller).
    /// </summary>
    [IdlMember("targetOrigin", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-windowpostmessageoptions-targetorigin")]
    public string TargetOrigin { get; init; } = "/";
}
