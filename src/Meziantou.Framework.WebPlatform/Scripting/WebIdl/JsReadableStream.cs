namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// The IDL value of a <c>ReadableStream</c> (https://streams.spec.whatwg.org/#rs-class) given to a .NET member: the
/// streams are implemented in JavaScript (Streams.js), so a .NET member reads the chunks of the stream through its realm
/// (e.g. the <c>data</c> of an <c>ImageDecoder</c>), or only tells that its argument is a stream (e.g.
/// <c>navigator.sendBeacon()</c> rejects the stream bodies).
/// </summary>
internal sealed class JsReadableStream(IJsReadableStreamSource source)
{
    /// <summary>The operations of the realm of the stream.</summary>
    public IJsReadableStreamSource Source { get; } = source;
}

/// <summary>The operations on a <c>ReadableStream</c> of the scripts, implemented by the realm of the stream.</summary>
internal interface IJsReadableStreamSource
{
    /// <summary>Whether the stream is locked to a reader (https://streams.spec.whatwg.org/#is-readable-stream-locked).</summary>
    bool IsLocked { get; }

    /// <summary>Whether the stream has been read or canceled ([[disturbed]]).</summary>
    bool IsDisturbed { get; }

    /// <summary>
    /// Gets a reader for the stream (https://streams.spec.whatwg.org/#readablestream-get-a-reader) and reads its chunks until
    /// it is closed, errored or canceled, on the thread of the event loop of the realm: <paramref name="onChunk"/> is called
    /// with the bytes of each chunk, then <paramref name="onEnd"/> when the stream is closed, <paramref name="onError"/> when
    /// it is errored, or <paramref name="onInvalidChunk"/> when a chunk is not a <c>Uint8Array</c> (the stream is then
    /// canceled). The returned action cancels the stream; no callback is called after it.
    /// </summary>
    /// <exception cref="Dom.TypeErrorException">The stream is locked.</exception>
    Action ReadChunks(Action<byte[]> onChunk, Action onEnd, Action onError, Action onInvalidChunk);
}
