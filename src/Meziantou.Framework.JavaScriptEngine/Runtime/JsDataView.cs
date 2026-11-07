namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A DataView (https://tc39.es/ecma262/#sec-dataview-objects): [[ViewedArrayBuffer]], [[ByteLength]] (auto for a view
/// tracking the length of a resizable buffer) and [[ByteOffset]].
/// </summary>
internal sealed class JsDataView : JsObject
{
    private const int Auto = -1;

    public JsDataView(JsObject? prototype, JsArrayBuffer buffer, int byteOffset, int? byteLength)
        : base(prototype, ObjectClass.DataView)
    {
        ViewedArrayBuffer = buffer;
        ByteOffset = byteOffset;
        FixedByteLength = byteLength ?? Auto;
    }

    /// <summary>Gets the [[ViewedArrayBuffer]].</summary>
    public JsArrayBuffer ViewedArrayBuffer { get; }

    /// <summary>Gets the [[ByteOffset]].</summary>
    public int ByteOffset { get; }

    /// <summary>Gets the [[ByteLength]], or -1 when it is auto.</summary>
    public int FixedByteLength { get; }

    /// <summary>Creates a DataView of the realm (the byte offset and the byte length must be valid; the byte length is null for a length-tracking view).</summary>
    public static JsDataView Create(JsRealm realm, JsArrayBuffer buffer, int byteOffset, int? byteLength) => new(realm.Intrinsics.DataViewPrototype, buffer, byteOffset, byteLength);

    /// <summary>IsViewOutOfBounds + GetViewByteLength (MakeDataViewWithBufferWitnessRecord): false when the view is out of bounds or its buffer is detached.</summary>
    public bool TryGetByteLength(out int byteLength)
    {
        var buffer = ViewedArrayBuffer;
        if (buffer.IsDetached)
        {
            byteLength = 0;
            return false;
        }

        var bufferByteLength = buffer.ByteLength;
        var byteOffsetStart = ByteOffset;
        if (byteOffsetStart > bufferByteLength)
        {
            byteLength = 0;
            return false;
        }

        if (FixedByteLength == Auto)
        {
            byteLength = bufferByteLength - byteOffsetStart;
            return true;
        }

        if ((long)byteOffsetStart + FixedByteLength > bufferByteLength)
        {
            byteLength = 0;
            return false;
        }

        byteLength = FixedByteLength;
        return true;
    }

    /// <summary>Gets the bytes of the view (empty when it is out of bounds). It does not run JavaScript code.</summary>
    public Span<byte> GetSpan()
    {
        if (!TryGetByteLength(out var byteLength))
            return [];

        return ViewedArrayBuffer.GetSpan().Slice(ByteOffset, byteLength);
    }
}
