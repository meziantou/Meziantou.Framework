namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    private readonly JsFunction[] _typedArrayConstructors = new JsFunction[BinaryDataOperations.ElementTypeCount];
    private readonly JsObject[] _typedArrayPrototypes = new JsObject[BinaryDataOperations.ElementTypeCount];

    /// <summary>%ArrayBuffer%</summary>
    public JsFunction ArrayBuffer { get; private set; } = null!;

    /// <summary>%ArrayBuffer.prototype%</summary>
    public JsObject ArrayBufferPrototype { get; private set; } = null!;

    /// <summary>%SharedArrayBuffer%</summary>
    public JsFunction SharedArrayBuffer { get; private set; } = null!;

    /// <summary>%SharedArrayBuffer.prototype%</summary>
    public JsObject SharedArrayBufferPrototype { get; private set; } = null!;

    /// <summary>%DataView%</summary>
    public JsFunction DataView { get; private set; } = null!;

    /// <summary>%DataView.prototype%</summary>
    public JsObject DataViewPrototype { get; private set; } = null!;

    /// <summary>%TypedArray%</summary>
    public JsFunction TypedArray { get; private set; } = null!;

    /// <summary>%TypedArray.prototype%</summary>
    public JsObject TypedArrayPrototype { get; private set; } = null!;

    /// <summary>%Atomics%</summary>
    public JsObject Atomics { get; private set; } = null!;

    /// <summary>Gets the constructor of the element type (e.g. %Int8Array%).</summary>
    public JsFunction GetTypedArrayConstructor(TypedArrayElementType type) => _typedArrayConstructors[(int)type];

    /// <summary>Gets the prototype of the element type (e.g. %Int8Array.prototype%).</summary>
    public JsObject GetTypedArrayPrototype(TypedArrayElementType type) => _typedArrayPrototypes[(int)type];

    partial void InitializeBinaryData()
    {
        ArrayBufferPrototype = new JsObject(ObjectPrototype);
        SharedArrayBufferPrototype = new JsObject(ObjectPrototype);
        DataViewPrototype = new JsObject(ObjectPrototype);
        TypedArrayPrototype = new JsObject(ObjectPrototype);
        foreach (var type in Enum.GetValues<TypedArrayElementType>())
        {
            _typedArrayPrototypes[(int)type] = new JsObject(TypedArrayPrototype);
        }

        ArrayBuffer = ArrayBufferBuiltins.Initialize(this);
        SharedArrayBuffer = ArrayBufferBuiltins.InitializeShared(this);
        DataView = DataViewBuiltins.Initialize(this);
        TypedArray = TypedArrayBuiltins.Initialize(this, _typedArrayConstructors);
        Atomics = AtomicsBuiltins.Initialize(this);
    }
}
