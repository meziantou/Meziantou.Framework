namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// The IDL value of a <c>ReadableStream</c> (https://streams.spec.whatwg.org/#rs-class) given to a .NET member: the
/// streams are implemented in JavaScript (Streams.js), so a .NET member can only tell that its argument is a stream (e.g.
/// <c>navigator.sendBeacon()</c> rejects the stream bodies).
/// </summary>
internal sealed class JsReadableStream
{
    public static JsReadableStream Instance { get; } = new();

    private JsReadableStream()
    {
    }
}
