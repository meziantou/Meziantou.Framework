using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.WebCodecs;

/// <summary>The buffer sources given to the WebCodecs objects and their transfer lists (e.g. <c>AudioDataInit.transfer</c>).</summary>
internal static class WebCodecsBuffers
{
    /// <summary>Returns the bytes of a buffer source of the scripts, a <see cref="byte"/> array or a memory of bytes.</summary>
    /// <exception cref="TypeErrorException">The value is not a buffer.</exception>
    public static ReadOnlyMemory<byte> GetBytes(object? data) => data switch
    {
        JsBufferSource source => source.CurrentBytes,
        byte[] array => array,
        ReadOnlyMemory<byte> memory => memory,
        Memory<byte> memory => memory,
        ArraySegment<byte> segment => segment,
        _ => throw new TypeErrorException("The data is not a buffer source"),
    };

    /// <summary>Returns whether a buffer source of the scripts is detached.</summary>
    public static bool IsDetached(object? data) => data is JsBufferSource { IsDetached: true };

    /// <summary>
    /// Checks the <c>transfer</c> member of an init dictionary: a buffer given twice or a detached buffer throws a
    /// <c>DataCloneError</c> (e.g. https://w3c.github.io/webcodecs/#dom-audiodata-audiodata, step 3).
    /// </summary>
    public static IReadOnlyList<JsBufferSource> ValidateTransfer(IReadOnlyList<object>? transfer)
    {
        if (transfer is null || transfer.Count == 0)
            return [];

        var result = new List<JsBufferSource>(transfer.Count);
        foreach (var item in transfer)
        {
            if (item is not JsBufferSource source)
                continue;

            if (source.IsDetached)
                throw new DomException("A buffer of the transfer list is detached", "DataCloneError");

            if (source.Value is { } value && result.Exists(other => Equals(other.Value, value)))
                throw new DomException("A buffer is given twice in the transfer list", "DataCloneError");

            result.Add(source);
        }

        return result;
    }

    /// <summary>Detaches the buffers of a transfer list once their data is used by the object.</summary>
    public static void DetachTransfer(IReadOnlyList<JsBufferSource> transfer)
    {
        foreach (var source in transfer)
        {
            source.TryDetach();
        }
    }
}
