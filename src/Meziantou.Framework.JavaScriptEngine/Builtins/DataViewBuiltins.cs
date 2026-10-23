namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The DataView constructor and prototype (https://tc39.es/ecma262/#sec-dataview-objects).</summary>
internal static class DataViewBuiltins
{
    public static JsBuiltinFunction Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.DataViewPrototype;
        var constructor = realm.CreateBuiltinFunction("DataView", 1, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);

        prototype.DefineBuiltIn("constructor", constructor);
        realm.DefineAccessor(prototype, "buffer", GetBuffer, setter: null);
        realm.DefineAccessor(prototype, "byteLength", GetByteLength, setter: null);
        realm.DefineAccessor(prototype, "byteOffset", GetByteOffset, setter: null);
        DefineAccessors(realm, prototype, "BigInt64", TypedArrayElementType.BigInt64);
        DefineAccessors(realm, prototype, "BigUint64", TypedArrayElementType.BigUint64);
        DefineAccessors(realm, prototype, "Float16", TypedArrayElementType.Float16);
        DefineAccessors(realm, prototype, "Float32", TypedArrayElementType.Float32);
        DefineAccessors(realm, prototype, "Float64", TypedArrayElementType.Float64);
        DefineAccessors(realm, prototype, "Int8", TypedArrayElementType.Int8);
        DefineAccessors(realm, prototype, "Int16", TypedArrayElementType.Int16);
        DefineAccessors(realm, prototype, "Int32", TypedArrayElementType.Int32);
        DefineAccessors(realm, prototype, "Uint8", TypedArrayElementType.Uint8);
        DefineAccessors(realm, prototype, "Uint16", TypedArrayElementType.Uint16);
        DefineAccessors(realm, prototype, "Uint32", TypedArrayElementType.Uint32);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "DataView", PropertyFlags.Configurable);

        intrinsics.AddGlobal("DataView", constructor);
        return constructor;
    }

    private static void DefineAccessors(JsRealm realm, JsObject prototype, string name, TypedArrayElementType type)
    {
        var getName = "get" + name;
        var setName = "set" + name;
        realm.DefineMethod(prototype, getName, 1, (_, thisValue, arguments, _) => GetViewValue(ThisDataView(thisValue, getName), Arg(arguments, 0), Arg(arguments, 1), type));
        realm.DefineMethod(prototype, setName, 2, (_, thisValue, arguments, _) => SetViewValue(ThisDataView(thisValue, setName), Arg(arguments, 0), Arg(arguments, 2), type, Arg(arguments, 1)));
    }

    private static JsValue Arg(ReadOnlySpan<JsValue> arguments, int index) => arguments.Length > index ? arguments[index] : default;

    private static JsDataView ThisDataView(JsValue thisValue, string method)
    {
        if (thisValue.Cell is not JsDataView view)
            return ThrowHelper.ThrowTypeError<JsDataView>($"Method DataView.prototype.{method} called on incompatible receiver {JsOperations.DescribeForError(thisValue)}");

        return view;
    }

    /// <summary>DataView(buffer [, byteOffset [, byteLength]])</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor DataView requires 'new'");

        if (Arg(arguments, 0).Cell is not JsArrayBuffer buffer)
            return ThrowHelper.ThrowTypeError<JsValue>("First argument to DataView constructor must be an ArrayBuffer");

        var offset = JsOperations.ToIndex(Arg(arguments, 1));
        if (buffer.IsDetached)
            return ThrowHelper.ThrowTypeError<JsValue>("Cannot create a DataView on a detached ArrayBuffer");

        double bufferByteLength = buffer.ByteLength;
        if (offset > bufferByteLength)
            return ThrowHelper.ThrowRangeError<JsValue>($"Start offset {NumberFormatting.ToString(offset)} is outside the bounds of the buffer");

        var byteLengthArgument = Arg(arguments, 2);
        double? viewByteLength;
        if (byteLengthArgument.IsUndefined)
        {
            viewByteLength = buffer.IsFixedLength ? bufferByteLength - offset : null;
        }
        else
        {
            viewByteLength = JsOperations.ToIndex(byteLengthArgument);
            if (offset + viewByteLength > bufferByteLength)
                return ThrowHelper.ThrowRangeError<JsValue>($"Invalid DataView length {NumberFormatting.ToString(viewByteLength.Value)}");
        }

        var prototype = ObjectOperations.GetPrototypeFromConstructor(newTarget, static i => i.DataViewPrototype);

        // GetPrototypeFromConstructor may have detached or resized the buffer
        if (buffer.IsDetached)
            return ThrowHelper.ThrowTypeError<JsValue>("Cannot create a DataView on a detached ArrayBuffer");

        bufferByteLength = buffer.ByteLength;
        if (offset > bufferByteLength)
            return ThrowHelper.ThrowRangeError<JsValue>($"Start offset {NumberFormatting.ToString(offset)} is outside the bounds of the buffer");

        if (!byteLengthArgument.IsUndefined && offset + viewByteLength > bufferByteLength)
            return ThrowHelper.ThrowRangeError<JsValue>($"Invalid DataView length {NumberFormatting.ToString(viewByteLength!.Value)}");

        return new JsDataView(prototype, buffer, (int)offset, viewByteLength is { } length ? (int)length : null);
    }

    /// <summary>get DataView.prototype.buffer</summary>
    private static JsValue GetBuffer(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return ThisDataView(thisValue, "buffer").ViewedArrayBuffer;
    }

    /// <summary>get DataView.prototype.byteLength</summary>
    private static JsValue GetByteLength(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var view = ThisDataView(thisValue, "byteLength");
        if (!view.TryGetByteLength(out var byteLength))
            return ThrowHelper.ThrowTypeError<JsValue>("The DataView is detached or out of bounds");

        return byteLength;
    }

    /// <summary>get DataView.prototype.byteOffset</summary>
    private static JsValue GetByteOffset(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var view = ThisDataView(thisValue, "byteOffset");
        if (!view.TryGetByteLength(out _))
            return ThrowHelper.ThrowTypeError<JsValue>("The DataView is detached or out of bounds");

        return view.ByteOffset;
    }

    /// <summary>GetViewValue(view, requestIndex, isLittleEndian, type)</summary>
    private static JsValue GetViewValue(JsDataView view, JsValue requestIndex, JsValue littleEndian, TypedArrayElementType type)
    {
        var getIndex = JsOperations.ToIndex(requestIndex);
        var isLittleEndian = JsOperations.ToBoolean(littleEndian);
        var bufferIndex = GetBufferIndex(view, getIndex, type);
        return BinaryDataOperations.GetValueFromBuffer(view.ViewedArrayBuffer, bufferIndex, type, isLittleEndian);
    }

    /// <summary>SetViewValue(view, requestIndex, isLittleEndian, type, value)</summary>
    private static JsValue SetViewValue(JsDataView view, JsValue requestIndex, JsValue littleEndian, TypedArrayElementType type, JsValue value)
    {
        if (view.ViewedArrayBuffer.IsImmutable)
            return ThrowHelper.ThrowTypeError<JsValue>("Cannot write to a DataView backed by an immutable ArrayBuffer");

        var getIndex = JsOperations.ToIndex(requestIndex);
        var numericValue = BinaryDataOperations.ToNumericForElementType(type, value);
        var isLittleEndian = JsOperations.ToBoolean(littleEndian);
        var bufferIndex = GetBufferIndex(view, getIndex, type);
        BinaryDataOperations.SetValueInBuffer(view.ViewedArrayBuffer, bufferIndex, type, numericValue, isLittleEndian);
        return JsValue.Undefined;
    }

    private static int GetBufferIndex(JsDataView view, double getIndex, TypedArrayElementType type)
    {
        if (!view.TryGetByteLength(out var viewSize))
            return ThrowHelper.ThrowTypeError<int>("The DataView is detached or out of bounds");

        if (getIndex + BinaryDataOperations.GetElementSize(type) > viewSize)
            return ThrowHelper.ThrowRangeError<int>("Offset is outside the bounds of the DataView");

        return (int)getIndex + view.ByteOffset;
    }
}
