namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>Access to the bytes of the buffer sources (ArrayBuffer, SharedArrayBuffer, DataView and the typed arrays), without running JavaScript code.</summary>
public static class JsBinaryData
{
    /// <summary>
    /// Gets the kind and the bytes of a buffer source. <paramref name="bytes"/> is a view of the data block of the buffer
    /// (the writes are visible to JavaScript); it is empty when the buffer is detached or the view is out of bounds.
    /// Returns false for the other values.
    /// </summary>
    public static bool TryGetBufferSource(JsValue value, out JsBufferKind kind, out bool isShared, out Memory<byte> bytes)
    {
        switch (value.Cell)
        {
            case JsArrayBuffer buffer:
                isShared = buffer.IsShared;
                kind = isShared ? JsBufferKind.SharedArrayBuffer : JsBufferKind.ArrayBuffer;
                bytes = buffer.GetMemory();
                return true;

            case JsTypedArray typedArray:
                kind = JsBufferKind.Int8Array + (int)typedArray.ElementType;
                isShared = typedArray.ViewedArrayBuffer.IsShared;
                bytes = typedArray.TryGetLength(out var length) ? typedArray.ViewedArrayBuffer.GetMemory().Slice(typedArray.ByteOffset, length * typedArray.ElementSize) : default;
                return true;

            case JsDataView dataView:
                kind = JsBufferKind.DataView;
                isShared = dataView.ViewedArrayBuffer.IsShared;
                bytes = dataView.TryGetByteLength(out var byteLength) ? dataView.ViewedArrayBuffer.GetMemory().Slice(dataView.ByteOffset, byteLength) : default;
                return true;
        }

        kind = default;
        isShared = false;
        bytes = default;
        return false;
    }

    /// <summary>Gets the Shared Data Block of a SharedArrayBuffer, which <see cref="JsRealm.CreateSharedArrayBuffer"/> can share with a buffer of another engine (on another thread); null for the other values.</summary>
    public static JsSharedDataBlock? GetSharedDataBlock(JsValue value) => value.Cell is JsArrayBuffer { SharedBlock: { } block } ? block : null;

    /// <summary>Detaches an ArrayBuffer (DetachArrayBuffer(buffer, key)); the key is its [[ArrayBufferDetachKey]].</summary>
    /// <exception cref="ArgumentException">The value is not an ArrayBuffer.</exception>
    /// <exception cref="JsException">The key is not the detach key of the buffer (a TypeError).</exception>
    public static void DetachArrayBuffer(JsValue arrayBuffer, JsValue key = default)
    {
        if (arrayBuffer.Cell is not JsArrayBuffer { IsShared: false } buffer)
            throw new ArgumentException("The value is not an ArrayBuffer", nameof(arrayBuffer));

        buffer.Detach(key);
    }
}
