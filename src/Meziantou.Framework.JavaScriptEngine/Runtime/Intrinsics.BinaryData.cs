namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The ArrayBuffer, DataView, typed array and Atomics built-ins are created on first use (a global property, or an
/// intrinsic used by another built-in or by the host): they are about 15% of the built-ins of a realm, and most scripts do
/// not use them. The creation is not observable. SharedArrayBuffer is created with the realm: it is small, and a host
/// reads it when it creates a realm (HTML deletes the global property, and keeps the intrinsic).
/// </summary>
internal sealed partial class Intrinsics
{
    private readonly JsFunction[] _typedArrayConstructors = new JsFunction[BinaryDataOperations.ElementTypeCount];
    private readonly JsObject[] _typedArrayPrototypes = new JsObject[BinaryDataOperations.ElementTypeCount];
    private JsFunction? _arrayBuffer;
    private JsObject? _arrayBufferPrototype;
    private JsFunction? _dataView;
    private JsObject? _dataViewPrototype;
    private JsFunction? _typedArray;
    private JsObject? _typedArrayPrototype;
    private JsObject? _atomics;
    private bool _isBinaryDataInitializing;

    /// <summary>%ArrayBuffer%</summary>
    public JsFunction ArrayBuffer => EnsureBinaryData()._arrayBuffer!;

    /// <summary>%ArrayBuffer.prototype%</summary>
    public JsObject ArrayBufferPrototype => EnsureBinaryData()._arrayBufferPrototype!;

    /// <summary>%SharedArrayBuffer%</summary>
    public JsFunction SharedArrayBuffer { get; private set; } = null!;

    /// <summary>%SharedArrayBuffer.prototype%</summary>
    public JsObject SharedArrayBufferPrototype { get; private set; } = null!;

    /// <summary>%DataView%</summary>
    public JsFunction DataView => EnsureBinaryData()._dataView!;

    /// <summary>%DataView.prototype%</summary>
    public JsObject DataViewPrototype => EnsureBinaryData()._dataViewPrototype!;

    /// <summary>%TypedArray%</summary>
    public JsFunction TypedArray => EnsureBinaryData()._typedArray!;

    /// <summary>%TypedArray.prototype%</summary>
    public JsObject TypedArrayPrototype => EnsureBinaryData()._typedArrayPrototype!;

    /// <summary>%Atomics%</summary>
    public JsObject Atomics => EnsureBinaryData()._atomics!;

    /// <summary>Gets the constructor of the element type (e.g. %Int8Array%).</summary>
    public JsFunction GetTypedArrayConstructor(TypedArrayElementType type) => EnsureBinaryData()._typedArrayConstructors[(int)type];

    /// <summary>Gets the prototype of the element type (e.g. %Int8Array.prototype%).</summary>
    public JsObject GetTypedArrayPrototype(TypedArrayElementType type) => EnsureBinaryData()._typedArrayPrototypes[(int)type];

    // The global properties are lazy, in the order of the built-ins (the built-ins also add them with AddGlobal when they
    // are created, which is ignored once the global bindings are defined)
    partial void InitializeBinaryData()
    {
        AddLazyGlobal("ArrayBuffer", static intrinsics => intrinsics.ArrayBuffer);
        SharedArrayBufferPrototype = new JsObject(ObjectPrototype);
        SharedArrayBuffer = ArrayBufferBuiltins.InitializeShared(this);
        AddLazyGlobal("DataView", static intrinsics => intrinsics.DataView);
        foreach (var type in Enum.GetValues<TypedArrayElementType>())
        {
            AddLazyGlobal(BinaryDataOperations.GetTypedArrayName(type), TypedArrayConstructorFactories[(int)type]);
        }

        AddLazyGlobal("Atomics", static intrinsics => intrinsics.Atomics);
    }

    private static readonly Func<Intrinsics, JsValue>[] TypedArrayConstructorFactories = CreateTypedArrayConstructorFactories();

    private static Func<Intrinsics, JsValue>[] CreateTypedArrayConstructorFactories()
    {
        var factories = new Func<Intrinsics, JsValue>[BinaryDataOperations.ElementTypeCount];
        foreach (var type in Enum.GetValues<TypedArrayElementType>())
        {
            factories[(int)type] = intrinsics => intrinsics.GetTypedArrayConstructor(type);
        }

        return factories;
    }

    /// <summary>Creates the binary data built-ins, if they are not created yet.</summary>
    private Intrinsics EnsureBinaryData()
    {
        if (_atomics is null && !_isBinaryDataInitializing)
        {
            _isBinaryDataInitializing = true;
            InitializeBinaryDataObjects();
        }

        return this;
    }

    private void InitializeBinaryDataObjects()
    {
        // The prototypes exist before the constructors are created: the built-ins read them
        _arrayBufferPrototype = new JsObject(ObjectPrototype);
        _dataViewPrototype = new JsObject(ObjectPrototype);
        _typedArrayPrototype = new JsObject(ObjectPrototype);
        foreach (var type in Enum.GetValues<TypedArrayElementType>())
        {
            _typedArrayPrototypes[(int)type] = new JsObject(_typedArrayPrototype);
        }

        _arrayBuffer = ArrayBufferBuiltins.Initialize(this);
        _dataView = DataViewBuiltins.Initialize(this);
        _typedArray = TypedArrayBuiltins.Initialize(this, _typedArrayConstructors);
        _atomics = AtomicsBuiltins.Initialize(this);
    }
}
