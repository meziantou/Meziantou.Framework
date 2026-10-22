using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// %TypedArray%, %TypedArray.prototype% and the TypedArray constructors (https://tc39.es/ecma262/#sec-typedarray-objects).
/// </summary>
internal static class TypedArrayBuiltins
{
    private static readonly PropertyKey ToLocaleStringKey = new("toLocaleString");

    private static readonly BuiltinMethodTable ConstructorMethods =
    [
        new("from", 1, From),
        new("of", 0, Of),
    ];

    private static readonly BuiltinMethodTable PrototypeMethods =
    [
        new("copyWithin", 2, CopyWithin),
        new("entries", 0, Entries),
        new("every", 1, Every),
        new("fill", 1, Fill),
        new("filter", 1, Filter),
        new("find", 1, Find),
        new("findIndex", 1, FindIndex),
        new("findLast", 1, FindLast),
        new("findLastIndex", 1, FindLastIndex),
        new("forEach", 1, ForEach),
        new("includes", 1, Includes),
        new("indexOf", 1, IndexOf),
        new("join", 1, Join),
        new("keys", 0, Keys),
        new("lastIndexOf", 1, LastIndexOf),
    ];

    private static readonly BuiltinMethodTable PrototypeMethods2 =
    [
        new("map", 1, Map),
        new("reduce", 1, Reduce),
        new("reduceRight", 1, ReduceRight),
        new("reverse", 0, Reverse),
        new("set", 1, Set),
        new("slice", 2, Slice),
        new("some", 1, Some),
        new("sort", 1, Sort),
        new("subarray", 2, Subarray),
        new("toLocaleString", 0, ToLocaleString),
        new("toReversed", 0, ToReversed),
        new("toSorted", 1, ToSorted),
    ];

    public static JsBuiltinFunction Initialize(Intrinsics intrinsics, JsFunction[] constructors)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.TypedArrayPrototype;
        var constructor = realm.CreateBuiltinFunction("TypedArray", 0, ConstructAbstract, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        realm.DefineMethods(constructor, ConstructorMethods);
        realm.DefineAccessor(constructor, WellKnownSymbols.Species, GetSpecies, setter: null);

        prototype.DefineBuiltIn("constructor", constructor);
        realm.DefineMethod(prototype, "at", 1, At);
        realm.DefineAccessor(prototype, "buffer", GetBuffer, setter: null);
        realm.DefineAccessor(prototype, "byteLength", GetByteLength, setter: null);
        realm.DefineAccessor(prototype, "byteOffset", GetByteOffset, setter: null);
        realm.DefineMethods(prototype, PrototypeMethods);
        realm.DefineAccessor(prototype, "length", GetLength, setter: null);
        realm.DefineMethods(prototype, PrototypeMethods2);

        // %TypedArray%.prototype.toString is %Array.prototype.toString%
        prototype.DefineBuiltIn("toString", intrinsics.ArrayPrototypeToString);

        var values = realm.DefineEagerMethod(prototype, "values", 0, Values);
        realm.DefineMethod(prototype, "with", 2, With);
        prototype.DefineBuiltIn(WellKnownSymbols.Iterator, values);
        realm.DefineAccessor(prototype, WellKnownSymbols.ToStringTag, GetToStringTag, setter: null);

        foreach (var type in Enum.GetValues<TypedArrayElementType>())
        {
            constructors[(int)type] = CreateConstructor(intrinsics, constructor, type);
        }

        Uint8ArrayBase64Builtins.Initialize(intrinsics);
        return constructor;
    }

    private static JsBuiltinFunction CreateConstructor(Intrinsics intrinsics, JsFunction typedArrayConstructor, TypedArrayElementType type)
    {
        var realm = intrinsics.Realm;
        var name = BinaryDataOperations.GetTypedArrayName(type);
        var elementSize = BinaryDataOperations.GetElementSize(type);
        var prototype = intrinsics.GetTypedArrayPrototype(type);
        var constructor = realm.CreateBuiltinFunction(name, 3, (function, _, arguments, newTarget) => Construct(function, arguments, newTarget, type), isConstructor: true, typedArrayConstructor);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        constructor.DefineBuiltIn("BYTES_PER_ELEMENT", elementSize, PropertyFlags.None);
        prototype.DefineBuiltIn("constructor", constructor);
        prototype.DefineBuiltIn("BYTES_PER_ELEMENT", elementSize, PropertyFlags.None);
        intrinsics.AddGlobal(name, constructor);
        return constructor;
    }

    private static JsValue Arg(ReadOnlySpan<JsValue> arguments, int index) => arguments.Length > index ? arguments[index] : default;

    // ------------------------------------------------------------------------------------------------------------------
    // Abstract operations
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>RequireInternalSlot(O, [[TypedArrayName]])</summary>
    private static JsTypedArray ThisTypedArray(JsValue thisValue, string method)
    {
        if (thisValue.Cell is not JsTypedArray typedArray)
            return ThrowHelper.ThrowTypeError<JsTypedArray>($"Method %TypedArray%.prototype.{method} called on incompatible receiver {JsOperations.DescribeForError(thisValue)}");

        return typedArray;
    }

    /// <summary>ValidateTypedArray(O, seq-cst) and TypedArrayLength(taRecord)</summary>
    private static JsTypedArray ValidateTypedArray(JsValue thisValue, string method, out int length)
    {
        var typedArray = ThisTypedArray(thisValue, method);
        if (!typedArray.TryGetLength(out length))
            return ThrowHelper.ThrowTypeError<JsTypedArray>($"Cannot perform %TypedArray%.prototype.{method} on a detached or out-of-bounds TypedArray");

        return typedArray;
    }

    /// <summary>ValidateTypedArray(O, order) for a value: the typed array and its length.</summary>
    internal static JsTypedArray ValidateTypedArray(JsValue value, out int length)
    {
        length = 0;
        if (value.Cell is not JsTypedArray typedArray)
            return ThrowHelper.ThrowTypeError<JsTypedArray>($"{JsOperations.DescribeForError(value)} is not a TypedArray");

        if (!typedArray.TryGetLength(out length))
            return ThrowHelper.ThrowTypeError<JsTypedArray>("Cannot perform the operation on a detached or out-of-bounds TypedArray");

        return typedArray;
    }

    private static JsObject RequireCallable(JsValue value)
    {
        if (value.Cell is not JsObject { IsCallable: true } callable)
            return ThrowHelper.ThrowTypeError<JsObject>($"{JsOperations.DescribeForError(value)} is not a function");

        return callable;
    }

    /// <summary>AllocateTypedArray(constructorName, newTarget, defaultProto [, length])</summary>
    private static JsTypedArray AllocateTypedArray(JsRealm realm, TypedArrayElementType type, JsObject newTarget, double? length)
    {
        var prototype = ObjectOperations.GetPrototypeFromConstructor(newTarget, i => i.GetTypedArrayPrototype(type));
        var typedArray = new JsTypedArray(prototype, type);
        if (length is { } value)
        {
            AllocateTypedArrayBuffer(realm, typedArray, value);
        }

        return typedArray;
    }

    /// <summary>AllocateTypedArrayBuffer(O, length)</summary>
    private static void AllocateTypedArrayBuffer(JsRealm realm, JsTypedArray typedArray, double length)
    {
        var byteLength = typedArray.ElementSize * length;
        var data = JsArrayBuffer.Create(realm, byteLength);
        typedArray.Initialize(data, 0, (int)length);
    }

    /// <summary>Creates a typed array of the element type with a new buffer (TypedArrayCreateSameType with the intrinsic constructor of the realm).</summary>
    internal static JsTypedArray CreateTypedArray(JsRealm realm, TypedArrayElementType type, double length)
    {
        var typedArray = new JsTypedArray(realm.Intrinsics.GetTypedArrayPrototype(type), type);
        AllocateTypedArrayBuffer(realm, typedArray, length);
        return typedArray;
    }

    /// <summary>TypedArrayCreateFromConstructor(constructor, argumentList)</summary>
    private static JsTypedArray TypedArrayCreateFromConstructor(JsObject constructor, ReadOnlySpan<JsValue> arguments)
    {
        var newTypedArray = JsOperations.Construct(constructor, arguments);
        var typedArray = ValidateTypedArray(newTypedArray, out var length);
        if (arguments.Length == 1 && arguments[0].IsNumber && length < arguments[0].NumberValue)
            return ThrowHelper.ThrowTypeError<JsTypedArray>("The TypedArray created by the constructor is too small");

        return typedArray;
    }

    /// <summary>TypedArraySpeciesCreate(exemplar, argumentList)</summary>
    private static JsTypedArray TypedArraySpeciesCreate(JsRealm realm, JsTypedArray exemplar, ReadOnlySpan<JsValue> arguments)
    {
        var defaultConstructor = realm.Intrinsics.GetTypedArrayConstructor(exemplar.ElementType);
        var constructor = ObjectOperations.SpeciesConstructor(exemplar, defaultConstructor);
        JsTypedArray result;
        if (ReferenceEquals(constructor, defaultConstructor) && arguments.Length == 1 && arguments[0].IsNumber)
        {
            // Not observable: the prototype property of the constructor is not writable nor configurable
            result = CreateTypedArray(realm, exemplar.ElementType, arguments[0].NumberValue);
        }
        else
        {
            result = TypedArrayCreateFromConstructor(constructor, arguments);
        }

        if (result.IsBigInt != exemplar.IsBigInt)
            return ThrowHelper.ThrowTypeError<JsTypedArray>("The content type of the TypedArray created by the species constructor does not match");

        return result;
    }

    /// <summary>TypedArrayCreateSameType(exemplar, [length])</summary>
    private static JsTypedArray TypedArrayCreateSameType(JsRealm realm, JsTypedArray exemplar, int length) => CreateTypedArray(realm, exemplar.ElementType, length);

    /// <summary>The element at an index (! Get(O, ! ToString(k))): undefined when the index is no longer valid.</summary>
    private static JsValue GetElement(JsTypedArray typedArray, long index) => typedArray.GetElementOrUndefined(index);

    // ------------------------------------------------------------------------------------------------------------------
    // Constructors
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>%TypedArray%(): throws a TypeError.</summary>
    private static JsValue ConstructAbstract(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return ThrowHelper.ThrowTypeError<JsValue>("Abstract class TypedArray not directly constructable");
    }

    /// <summary>TypedArray(...args)</summary>
    private static JsValue Construct(JsBuiltinFunction function, ReadOnlySpan<JsValue> arguments, JsObject? newTarget, TypedArrayElementType type)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>($"Constructor {BinaryDataOperations.GetTypedArrayName(type)} requires 'new'");

        var realm = function.Realm;
        if (arguments.Length == 0)
            return AllocateTypedArray(realm, type, newTarget, 0);

        var firstArgument = arguments[0];
        if (firstArgument.Cell is JsObject firstObject)
        {
            var typedArray = AllocateTypedArray(realm, type, newTarget, length: null);
            switch (firstObject)
            {
                case JsTypedArray source:
                    InitializeFromTypedArray(realm, typedArray, source);
                    break;
                case JsArrayBuffer buffer:
                    InitializeFromArrayBuffer(typedArray, buffer, Arg(arguments, 1), Arg(arguments, 2));
                    break;
                default:
                    var usingIterator = JsOperations.GetMethod(firstArgument, new PropertyKey(WellKnownSymbols.Iterator));
                    if (usingIterator is not null)
                    {
                        var values = IteratorOperations.IteratorToList(IteratorOperations.GetIteratorFromMethod(firstArgument, usingIterator));
                        AllocateTypedArrayBuffer(realm, typedArray, values.Count);
                        for (var k = 0; k < values.Count; k++)
                        {
                            typedArray.SetElementFromValue(k, values[k]);
                        }
                    }
                    else
                    {
                        var length = JsOperations.LengthOfArrayLike(firstObject);
                        AllocateTypedArrayBuffer(realm, typedArray, length);
                        for (var k = 0L; k < length; k++)
                        {
                            var value = firstObject.Get(PropertyKey.FromInteger(k));
                            typedArray.SetElementFromValue(k, value);
                        }
                    }

                    break;
            }

            return typedArray;
        }

        var elementLength = JsOperations.ToIndex(firstArgument);
        return AllocateTypedArray(realm, type, newTarget, elementLength);
    }

    /// <summary>InitializeTypedArrayFromTypedArray(O, srcArray)</summary>
    private static void InitializeFromTypedArray(JsRealm realm, JsTypedArray typedArray, JsTypedArray source)
    {
        if (!source.TryGetLength(out var elementLength))
        {
            ThrowHelper.ThrowTypeError("Cannot construct a TypedArray from a detached or out-of-bounds TypedArray");
            return;
        }

        var byteLength = (double)typedArray.ElementSize * elementLength;
        var data = JsArrayBuffer.Create(realm, byteLength);
        if (typedArray.ElementType == source.ElementType)
        {
            source.GetSpan().CopyTo(data.GetSpan());
        }
        else
        {
            if (typedArray.IsBigInt != source.IsBigInt)
            {
                ThrowHelper.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions");
                return;
            }

            ConvertElements(source.ElementType, source.GetSpan(), typedArray.ElementType, data.GetSpan(), elementLength);
        }

        typedArray.Initialize(data, 0, elementLength);
    }

    /// <summary>Converts elements of a type to elements of another type with the same content type (GetValueFromBuffer + SetValueInBuffer for each element).</summary>
    private static void ConvertElements(TypedArrayElementType sourceType, ReadOnlySpan<byte> source, TypedArrayElementType targetType, Span<byte> target, int count)
    {
        var sourceSize = BinaryDataOperations.GetElementSize(sourceType);
        var targetSize = BinaryDataOperations.GetElementSize(targetType);
        var littleEndian = BinaryDataOperations.IsLittleEndian;
        if (BinaryDataOperations.IsBigIntElementType(sourceType))
        {
            // BigInt64 <-> BigUint64: the bits are the same
            source[..(count * sourceSize)].CopyTo(target);
            return;
        }

        for (var i = 0; i < count; i++)
        {
            var value = BinaryDataOperations.RawBytesToNumeric(sourceType, source[(i * sourceSize)..], littleEndian);
            BinaryDataOperations.NumberToRawBytes(targetType, value.NumberValue, target[(i * targetSize)..], littleEndian);
        }
    }

    /// <summary>InitializeTypedArrayFromArrayBuffer(O, buffer, byteOffset, length)</summary>
    private static void InitializeFromArrayBuffer(JsTypedArray typedArray, JsArrayBuffer buffer, JsValue byteOffset, JsValue length)
    {
        var elementSize = typedArray.ElementSize;
        var offset = JsOperations.ToIndex(byteOffset);
        if (offset % elementSize != 0)
        {
            ThrowHelper.ThrowRangeError($"Start offset of {typedArray.TypedArrayName} should be a multiple of {elementSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            return;
        }

        var bufferIsFixedLength = buffer.IsFixedLength;
        double newLength = 0;
        if (!length.IsUndefined)
        {
            newLength = JsOperations.ToIndex(length);
        }

        if (buffer.IsDetached)
        {
            ThrowHelper.ThrowTypeError("Cannot construct a TypedArray on a detached ArrayBuffer");
            return;
        }

        var bufferByteLength = buffer.ByteLength;
        if (length.IsUndefined && !bufferIsFixedLength)
        {
            if (offset > bufferByteLength)
            {
                ThrowHelper.ThrowRangeError($"Start offset {NumberFormatting.ToString(offset)} is outside the bounds of the buffer");
                return;
            }

            typedArray.Initialize(buffer, (int)offset, length: null);
            return;
        }

        double newByteLength;
        if (length.IsUndefined)
        {
            if (bufferByteLength % elementSize != 0)
            {
                ThrowHelper.ThrowRangeError($"Byte length of {typedArray.TypedArrayName} should be a multiple of {elementSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                return;
            }

            newByteLength = bufferByteLength - offset;
            if (newByteLength < 0)
            {
                ThrowHelper.ThrowRangeError($"Start offset {NumberFormatting.ToString(offset)} is outside the bounds of the buffer");
                return;
            }
        }
        else
        {
            newByteLength = newLength * elementSize;
            if (offset + newByteLength > bufferByteLength)
            {
                ThrowHelper.ThrowRangeError($"Invalid typed array length: {NumberFormatting.ToString(newLength)}");
                return;
            }
        }

        typedArray.Initialize(buffer, (int)offset, (int)(newByteLength / elementSize));
    }

    // ------------------------------------------------------------------------------------------------------------------
    // %TypedArray%
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>%TypedArray%.from(source [, mapper [, thisArg]])</summary>
    private static JsValue From(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsObject { IsConstructor: true } constructor)
            return ThrowHelper.ThrowTypeError<JsValue>($"{JsOperations.DescribeForError(thisValue)} is not a constructor");

        var source = Arg(arguments, 0);
        var mapper = Arg(arguments, 1);
        var thisArg = Arg(arguments, 2);
        JsObject? mapperFunction = null;
        if (!mapper.IsUndefined)
        {
            mapperFunction = RequireCallable(mapper);
        }

        var usingIterator = JsOperations.GetMethod(source, new PropertyKey(WellKnownSymbols.Iterator));
        if (usingIterator is not null)
        {
            var values = IteratorOperations.IteratorToList(IteratorOperations.GetIteratorFromMethod(source, usingIterator));
            var target = TypedArrayCreateFromConstructor(constructor, [values.Count]);
            for (var k = 0; k < values.Count; k++)
            {
                var value = values[k];
                if (mapperFunction is not null)
                {
                    value = mapperFunction.Call(thisArg, [value, k]);
                }

                target.Set(new PropertyKey((uint)k), value, throwOnFailure: true);
            }

            return target;
        }

        var arrayLike = JsOperations.ToObject(source);
        var length = JsOperations.LengthOfArrayLike(arrayLike);
        var targetObject = TypedArrayCreateFromConstructor(constructor, [(double)length]);
        for (var k = 0L; k < length; k++)
        {
            var key = PropertyKey.FromInteger(k);
            var value = arrayLike.Get(key);
            if (mapperFunction is not null)
            {
                value = mapperFunction.Call(thisArg, [value, (double)k]);
            }

            targetObject.Set(key, value, throwOnFailure: true);
        }

        return targetObject;
    }

    /// <summary>%TypedArray%.of(...items)</summary>
    private static JsValue Of(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsObject { IsConstructor: true } constructor)
            return ThrowHelper.ThrowTypeError<JsValue>($"{JsOperations.DescribeForError(thisValue)} is not a constructor");

        var length = arguments.Length;
        var items = arguments.ToArray();
        var newObject = TypedArrayCreateFromConstructor(constructor, [length]);
        for (var k = 0; k < length; k++)
        {
            newObject.Set(new PropertyKey((uint)k), items[k], throwOnFailure: true);
        }

        return newObject;
    }

    /// <summary>get %TypedArray%[%Symbol.species%]</summary>
    private static JsValue GetSpecies(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget) => thisValue;

    // ------------------------------------------------------------------------------------------------------------------
    // %TypedArray%.prototype
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>%TypedArray%.prototype.at(index)</summary>
    private static JsValue At(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateTypedArray(thisValue, "at", out var length);
        var relativeIndex = JsOperations.ToIntegerOrInfinity(Arg(arguments, 0));
        var k = relativeIndex >= 0 ? relativeIndex : length + relativeIndex;
        if (k < 0 || k >= length)
            return JsValue.Undefined;

        return typedArray.GetElementOrUndefined(k);
    }

    /// <summary>get %TypedArray%.prototype.buffer</summary>
    private static JsValue GetBuffer(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return ThisTypedArray(thisValue, "buffer").ViewedArrayBuffer;
    }

    /// <summary>get %TypedArray%.prototype.byteLength</summary>
    private static JsValue GetByteLength(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return ThisTypedArray(thisValue, "byteLength").GetByteLengthOrZero();
    }

    /// <summary>get %TypedArray%.prototype.byteOffset</summary>
    private static JsValue GetByteOffset(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ThisTypedArray(thisValue, "byteOffset");
        if (typedArray.IsOutOfBounds)
            return 0;

        return typedArray.ByteOffset;
    }

    /// <summary>get %TypedArray%.prototype.length</summary>
    private static JsValue GetLength(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return ThisTypedArray(thisValue, "length").GetLengthOrZero();
    }

    /// <summary>get %TypedArray%.prototype[%Symbol.toStringTag%]</summary>
    private static JsValue GetToStringTag(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsTypedArray typedArray)
            return JsValue.Undefined;

        return typedArray.TypedArrayName;
    }

    /// <summary>%TypedArray%.prototype.copyWithin(target, start [, end])</summary>
    private static JsValue CopyWithin(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateTypedArray(thisValue, "copyWithin", out var length);
        var targetIndex = BinaryDataOperations.ClampRelativeIndex(JsOperations.ToIntegerOrInfinity(Arg(arguments, 0)), length);
        var startIndex = BinaryDataOperations.ClampRelativeIndex(JsOperations.ToIntegerOrInfinity(Arg(arguments, 1)), length);
        var endIndex = BinaryDataOperations.ToClampedIndex(Arg(arguments, 2), length, length);
        var count = Math.Min(endIndex - startIndex, length - targetIndex);
        if (count > 0)
        {
            if (!typedArray.TryGetLength(out length))
                return ThrowHelper.ThrowTypeError<JsValue>("Cannot perform %TypedArray%.prototype.copyWithin on a detached or out-of-bounds TypedArray");

            // The arguments may have shrunk the buffer: the copy is truncated to the current length
            count = Math.Min(count, Math.Min(length - startIndex, length - targetIndex));
            if (count > 0)
            {
                var elementSize = typedArray.ElementSize;
                var byteOffset = typedArray.ByteOffset;
                var span = typedArray.ViewedArrayBuffer.GetSpan();
                span.Slice((int)((startIndex * elementSize) + byteOffset), (int)(count * elementSize)).CopyTo(span[(int)((targetIndex * elementSize) + byteOffset)..]);
            }
        }

        return typedArray;
    }

    /// <summary>%TypedArray%.prototype.entries()</summary>
    private static JsValue Entries(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateTypedArray(thisValue, "entries", out _);
        return JsArrayIterator.Create(function.Realm, typedArray, ArrayIteratorKind.Entries);
    }

    /// <summary>%TypedArray%.prototype.keys()</summary>
    private static JsValue Keys(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateTypedArray(thisValue, "keys", out _);
        return JsArrayIterator.Create(function.Realm, typedArray, ArrayIteratorKind.Keys);
    }

    /// <summary>%TypedArray%.prototype.values() and %TypedArray%.prototype[%Symbol.iterator%]()</summary>
    private static JsValue Values(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateTypedArray(thisValue, "values", out _);
        return JsArrayIterator.Create(function.Realm, typedArray, ArrayIteratorKind.Values);
    }

    /// <summary>%TypedArray%.prototype.every(callback [, thisArg])</summary>
    private static JsValue Every(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateTypedArray(thisValue, "every", out var length);
        var callback = RequireCallable(Arg(arguments, 0));
        var thisArg = Arg(arguments, 1);
        for (var k = 0; k < length; k++)
        {
            var value = GetElement(typedArray, k);
            if (!JsOperations.ToBoolean(callback.Call(thisArg, [value, k, typedArray])))
                return false;
        }

        return true;
    }

    /// <summary>%TypedArray%.prototype.some(callback [, thisArg])</summary>
    private static JsValue Some(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateTypedArray(thisValue, "some", out var length);
        var callback = RequireCallable(Arg(arguments, 0));
        var thisArg = Arg(arguments, 1);
        for (var k = 0; k < length; k++)
        {
            var value = GetElement(typedArray, k);
            if (JsOperations.ToBoolean(callback.Call(thisArg, [value, k, typedArray])))
                return true;
        }

        return false;
    }

    /// <summary>%TypedArray%.prototype.forEach(callback [, thisArg])</summary>
    private static JsValue ForEach(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateTypedArray(thisValue, "forEach", out var length);
        var callback = RequireCallable(Arg(arguments, 0));
        var thisArg = Arg(arguments, 1);
        for (var k = 0; k < length; k++)
        {
            var value = GetElement(typedArray, k);
            callback.Call(thisArg, [value, k, typedArray]);
        }

        return JsValue.Undefined;
    }

    /// <summary>%TypedArray%.prototype.fill(value [, start [, end]])</summary>
    private static JsValue Fill(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateTypedArray(thisValue, "fill", out var length);
        var value = BinaryDataOperations.ToNumericForElementType(typedArray.ElementType, Arg(arguments, 0));
        var startIndex = BinaryDataOperations.ClampRelativeIndex(JsOperations.ToIntegerOrInfinity(Arg(arguments, 1)), length);
        var endIndex = BinaryDataOperations.ToClampedIndex(Arg(arguments, 2), length, length);
        if (!typedArray.TryGetLength(out length))
            return ThrowHelper.ThrowTypeError<JsValue>("Cannot perform %TypedArray%.prototype.fill on a detached or out-of-bounds TypedArray");

        endIndex = Math.Min(endIndex, length);
        if (startIndex < endIndex)
        {
            var elementSize = typedArray.ElementSize;
            Span<byte> encoded = stackalloc byte[8];
            BinaryDataOperations.NumericToRawBytes(typedArray.ElementType, value, encoded, BinaryDataOperations.IsLittleEndian);
            var span = typedArray.ViewedArrayBuffer.GetSpan().Slice(typedArray.ByteOffset + ((int)startIndex * elementSize), (int)(endIndex - startIndex) * elementSize);
            if (elementSize == 1)
            {
                span.Fill(encoded[0]);
            }
            else
            {
                var element = encoded[..elementSize];
                for (var i = 0; i < span.Length; i += elementSize)
                {
                    element.CopyTo(span[i..]);
                }
            }
        }

        return typedArray;
    }

    /// <summary>%TypedArray%.prototype.filter(callback [, thisArg])</summary>
    private static JsValue Filter(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateTypedArray(thisValue, "filter", out var length);
        var callback = RequireCallable(Arg(arguments, 0));
        var thisArg = Arg(arguments, 1);
        var kept = new List<JsValue>();
        for (var k = 0; k < length; k++)
        {
            var value = GetElement(typedArray, k);
            if (JsOperations.ToBoolean(callback.Call(thisArg, [value, k, typedArray])))
            {
                kept.Add(value);
            }
        }

        var result = TypedArraySpeciesCreate(function.Realm, typedArray, [kept.Count]);
        for (var n = 0; n < kept.Count; n++)
        {
            result.SetElementFromValue(n, kept[n]);
        }

        return result;
    }

    /// <summary>FindViaPredicate(O, len, direction, predicate, thisArg): the index, or -1.</summary>
    private static long FindViaPredicate(JsTypedArray typedArray, int length, bool ascending, ReadOnlySpan<JsValue> arguments, out JsValue value)
    {
        var predicate = RequireCallable(Arg(arguments, 0));
        var thisArg = Arg(arguments, 1);
        for (var i = 0; i < length; i++)
        {
            var k = ascending ? i : length - 1 - i;
            var kValue = GetElement(typedArray, k);
            if (JsOperations.ToBoolean(predicate.Call(thisArg, [kValue, k, typedArray])))
            {
                value = kValue;
                return k;
            }
        }

        value = JsValue.Undefined;
        return -1;
    }

    /// <summary>%TypedArray%.prototype.find(predicate [, thisArg])</summary>
    private static JsValue Find(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateTypedArray(thisValue, "find", out var length);
        FindViaPredicate(typedArray, length, ascending: true, arguments, out var value);
        return value;
    }

    /// <summary>%TypedArray%.prototype.findIndex(predicate [, thisArg])</summary>
    private static JsValue FindIndex(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateTypedArray(thisValue, "findIndex", out var length);
        return FindViaPredicate(typedArray, length, ascending: true, arguments, out _);
    }

    /// <summary>%TypedArray%.prototype.findLast(predicate [, thisArg])</summary>
    private static JsValue FindLast(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateTypedArray(thisValue, "findLast", out var length);
        FindViaPredicate(typedArray, length, ascending: false, arguments, out var value);
        return value;
    }

    /// <summary>%TypedArray%.prototype.findLastIndex(predicate [, thisArg])</summary>
    private static JsValue FindLastIndex(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateTypedArray(thisValue, "findLastIndex", out var length);
        return FindViaPredicate(typedArray, length, ascending: false, arguments, out _);
    }

    /// <summary>%TypedArray%.prototype.includes(searchElement [, fromIndex])</summary>
    private static JsValue Includes(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateTypedArray(thisValue, "includes", out var length);
        if (length == 0)
            return false;

        var n = JsOperations.ToIntegerOrInfinity(Arg(arguments, 1));
        if (double.IsPositiveInfinity(n))
            return false;

        var k = n >= 0 ? (long)n : (long)Math.Max(length + n, 0);
        var searchElement = Arg(arguments, 0);
        for (; k < length; k++)
        {
            if (JsOperations.SameValueZero(searchElement, GetElement(typedArray, k)))
                return true;
        }

        return false;
    }

    /// <summary>%TypedArray%.prototype.indexOf(searchElement [, fromIndex])</summary>
    private static JsValue IndexOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateTypedArray(thisValue, "indexOf", out var length);
        if (length == 0)
            return -1;

        var n = JsOperations.ToIntegerOrInfinity(Arg(arguments, 1));
        if (double.IsPositiveInfinity(n))
            return -1;

        var k = n >= 0 ? (long)n : (long)Math.Max(length + n, 0);
        var searchElement = Arg(arguments, 0);
        for (; k < length; k++)
        {
            // HasProperty(O, Pk) is false for the indices beyond the current length
            if (typedArray.IsValidIntegerIndex(k, out var index) && JsOperations.IsStrictlyEqual(searchElement, typedArray.GetElement(index)))
                return k;
        }

        return -1;
    }

    /// <summary>%TypedArray%.prototype.lastIndexOf(searchElement [, fromIndex])</summary>
    private static JsValue LastIndexOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateTypedArray(thisValue, "lastIndexOf", out var length);
        if (length == 0)
            return -1;

        var n = arguments.Length > 1 ? JsOperations.ToIntegerOrInfinity(arguments[1]) : length - 1;
        if (double.IsNegativeInfinity(n))
            return -1;

        var k = n >= 0 ? (long)Math.Min(n, length - 1) : (long)(length + n);
        var searchElement = Arg(arguments, 0);
        for (; k >= 0; k--)
        {
            if (typedArray.IsValidIntegerIndex(k, out var index) && JsOperations.IsStrictlyEqual(searchElement, typedArray.GetElement(index)))
                return k;
        }

        return -1;
    }

    /// <summary>%TypedArray%.prototype.join(separator)</summary>
    private static JsValue Join(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateTypedArray(thisValue, "join", out var length);
        var separatorValue = Arg(arguments, 0);
        var separator = separatorValue.IsUndefined ? "," : JsOperations.ToString(separatorValue);
        var builder = new StringBuilder();
        for (var k = 0; k < length; k++)
        {
            if (k > 0)
            {
                builder.Append(separator);
            }

            var element = GetElement(typedArray, k);
            if (!element.IsUndefined)
            {
                builder.Append(JsOperations.ToString(element));
            }

            if (builder.Length > JsString.MaxLength)
                return ThrowHelper.ThrowRangeError<JsValue>("Invalid string length");
        }

        return builder.ToString();
    }

    /// <summary>%TypedArray%.prototype.map(callback [, thisArg])</summary>
    private static JsValue Map(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateTypedArray(thisValue, "map", out var length);
        var callback = RequireCallable(Arg(arguments, 0));
        var thisArg = Arg(arguments, 1);
        var result = TypedArraySpeciesCreate(function.Realm, typedArray, [length]);
        for (var k = 0; k < length; k++)
        {
            var value = GetElement(typedArray, k);
            var mappedValue = callback.Call(thisArg, [value, k, typedArray]);
            result.Set(new PropertyKey((uint)k), mappedValue, throwOnFailure: true);
        }

        return result;
    }

    /// <summary>%TypedArray%.prototype.reduce(callback [, initialValue])</summary>
    private static JsValue Reduce(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateTypedArray(thisValue, "reduce", out var length);
        var callback = RequireCallable(Arg(arguments, 0));
        if (length == 0 && arguments.Length < 2)
            return ThrowHelper.ThrowTypeError<JsValue>("Reduce of empty array with no initial value");

        var k = 0;
        JsValue accumulator;
        if (arguments.Length >= 2)
        {
            accumulator = arguments[1];
        }
        else
        {
            accumulator = GetElement(typedArray, 0);
            k = 1;
        }

        for (; k < length; k++)
        {
            var value = GetElement(typedArray, k);
            accumulator = callback.Call(JsValue.Undefined, [accumulator, value, k, typedArray]);
        }

        return accumulator;
    }

    /// <summary>%TypedArray%.prototype.reduceRight(callback [, initialValue])</summary>
    private static JsValue ReduceRight(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateTypedArray(thisValue, "reduceRight", out var length);
        var callback = RequireCallable(Arg(arguments, 0));
        if (length == 0 && arguments.Length < 2)
            return ThrowHelper.ThrowTypeError<JsValue>("Reduce of empty array with no initial value");

        var k = length - 1;
        JsValue accumulator;
        if (arguments.Length >= 2)
        {
            accumulator = arguments[1];
        }
        else
        {
            accumulator = GetElement(typedArray, k);
            k--;
        }

        for (; k >= 0; k--)
        {
            var value = GetElement(typedArray, k);
            accumulator = callback.Call(JsValue.Undefined, [accumulator, value, k, typedArray]);
        }

        return accumulator;
    }

    /// <summary>%TypedArray%.prototype.reverse()</summary>
    private static JsValue Reverse(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateTypedArray(thisValue, "reverse", out var length);
        var elementSize = typedArray.ElementSize;
        var span = typedArray.ViewedArrayBuffer.GetSpan().Slice(typedArray.ByteOffset, length * elementSize);
        Span<byte> temp = stackalloc byte[8];
        for (int lower = 0, upper = length - 1; lower < upper; lower++, upper--)
        {
            var lowerBytes = span.Slice(lower * elementSize, elementSize);
            var upperBytes = span.Slice(upper * elementSize, elementSize);
            lowerBytes.CopyTo(temp);
            upperBytes.CopyTo(lowerBytes);
            temp[..elementSize].CopyTo(upperBytes);
        }

        return typedArray;
    }

    /// <summary>%TypedArray%.prototype.set(source [, offset])</summary>
    private static JsValue Set(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var target = ThisTypedArray(thisValue, "set");
        var targetOffset = JsOperations.ToIntegerOrInfinity(Arg(arguments, 1));
        if (targetOffset < 0)
            return ThrowHelper.ThrowRangeError<JsValue>("offset is out of bounds");

        var source = Arg(arguments, 0);
        if (source.Cell is JsTypedArray sourceTypedArray)
        {
            SetFromTypedArray(target, targetOffset, sourceTypedArray);
        }
        else
        {
            SetFromArrayLike(target, targetOffset, source);
        }

        return JsValue.Undefined;
    }

    /// <summary>SetTypedArrayFromTypedArray(target, targetOffset, source)</summary>
    private static void SetFromTypedArray(JsTypedArray target, double targetOffset, JsTypedArray source)
    {
        if (!target.TryGetLength(out var targetLength))
        {
            ThrowHelper.ThrowTypeError("Cannot perform %TypedArray%.prototype.set on a detached or out-of-bounds TypedArray");
            return;
        }

        if (!source.TryGetLength(out var sourceLength))
        {
            ThrowHelper.ThrowTypeError("Cannot perform %TypedArray%.prototype.set with a detached or out-of-bounds TypedArray");
            return;
        }

        if (double.IsPositiveInfinity(targetOffset) || sourceLength + targetOffset > targetLength)
        {
            ThrowHelper.ThrowRangeError("offset is out of bounds");
            return;
        }

        if (target.IsBigInt != source.IsBigInt)
        {
            ThrowHelper.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions");
            return;
        }

        var targetSpan = target.ViewedArrayBuffer.GetSpan()[(((int)targetOffset * target.ElementSize) + target.ByteOffset)..];
        ReadOnlySpan<byte> sourceSpan = source.ViewedArrayBuffer.GetSpan().Slice(source.ByteOffset, sourceLength * source.ElementSize);
        if (source.ElementType == target.ElementType)
        {
            // CopyTo handles the overlapping ranges of the same buffer (as if the source was cloned first)
            sourceSpan.CopyTo(targetSpan);
            return;
        }

        if (source.ViewedArrayBuffer.HasSameData(target.ViewedArrayBuffer))
        {
            sourceSpan = sourceSpan.ToArray();
        }

        ConvertElements(source.ElementType, sourceSpan, target.ElementType, targetSpan, sourceLength);
    }

    /// <summary>SetTypedArrayFromArrayLike(target, targetOffset, source)</summary>
    private static void SetFromArrayLike(JsTypedArray target, double targetOffset, JsValue source)
    {
        if (!target.TryGetLength(out var targetLength))
        {
            ThrowHelper.ThrowTypeError("Cannot perform %TypedArray%.prototype.set on a detached or out-of-bounds TypedArray");
            return;
        }

        var sourceObject = JsOperations.ToObject(source);
        var sourceLength = JsOperations.LengthOfArrayLike(sourceObject);
        if (double.IsPositiveInfinity(targetOffset) || sourceLength + targetOffset > targetLength)
        {
            ThrowHelper.ThrowRangeError("offset is out of bounds");
            return;
        }

        for (var k = 0L; k < sourceLength; k++)
        {
            var value = sourceObject.Get(PropertyKey.FromInteger(k));
            target.SetElementFromValue(targetOffset + k, value);
        }
    }

    /// <summary>%TypedArray%.prototype.slice(start, end)</summary>
    private static JsValue Slice(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateTypedArray(thisValue, "slice", out var sourceLength);
        var startIndex = BinaryDataOperations.ClampRelativeIndex(JsOperations.ToIntegerOrInfinity(Arg(arguments, 0)), sourceLength);
        var endIndex = BinaryDataOperations.ToClampedIndex(Arg(arguments, 1), sourceLength, sourceLength);
        var count = Math.Max(endIndex - startIndex, 0);
        var result = TypedArraySpeciesCreate(function.Realm, typedArray, [(double)count]);
        if (count > 0)
        {
            if (!typedArray.TryGetLength(out sourceLength))
                return ThrowHelper.ThrowTypeError<JsValue>("Cannot perform %TypedArray%.prototype.slice on a detached or out-of-bounds TypedArray");

            endIndex = Math.Min(endIndex, sourceLength);
            count = Math.Max(endIndex - startIndex, 0);
            if (typedArray.ElementType == result.ElementType)
            {
                var elementSize = typedArray.ElementSize;
                var sourceSpan = typedArray.ViewedArrayBuffer.GetSpan();
                var targetSpan = result.ViewedArrayBuffer.GetSpan();
                var sourceByteIndex = (int)(startIndex * elementSize) + typedArray.ByteOffset;
                var targetByteIndex = result.ByteOffset;
                var countBytes = (int)Math.Min(count * elementSize, targetSpan.Length - targetByteIndex);
                if (countBytes > 0)
                {
                    if (typedArray.ViewedArrayBuffer.HasSameData(result.ViewedArrayBuffer) && sourceByteIndex < targetByteIndex && targetByteIndex < sourceByteIndex + countBytes)
                    {
                        // The bytes are copied one by one, in ascending order
                        for (var i = 0; i < countBytes; i++)
                        {
                            targetSpan[targetByteIndex + i] = sourceSpan[sourceByteIndex + i];
                        }
                    }
                    else
                    {
                        sourceSpan.Slice(sourceByteIndex, countBytes).CopyTo(targetSpan[targetByteIndex..]);
                    }
                }
            }
            else
            {
                var n = 0;
                for (var k = startIndex; k < endIndex; k++, n++)
                {
                    var value = GetElement(typedArray, k);
                    result.SetElementFromValue(n, value);
                }
            }
        }

        return result;
    }

    /// <summary>%TypedArray%.prototype.sort(comparator)</summary>
    private static JsValue Sort(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var comparator = Arg(arguments, 0);
        if (!comparator.IsUndefined && !comparator.IsCallable)
            return ThrowHelper.ThrowTypeError<JsValue>("The comparison function must be either a function or undefined");

        var typedArray = ValidateTypedArray(thisValue, "sort", out var length);
        if (comparator.IsUndefined)
        {
            SortDefault(typedArray.ElementType, typedArray.ViewedArrayBuffer.GetSpan().Slice(typedArray.ByteOffset, length * typedArray.ElementSize));
            return typedArray;
        }

        var sorted = SortWithComparator(typedArray, length, comparator.AsObject());
        for (var j = 0; j < sorted.Length; j++)
        {
            typedArray.SetElementFromValue(j, sorted[j]);
        }

        return typedArray;
    }

    /// <summary>%TypedArray%.prototype.toSorted(comparator)</summary>
    private static JsValue ToSorted(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var comparator = Arg(arguments, 0);
        if (!comparator.IsUndefined && !comparator.IsCallable)
            return ThrowHelper.ThrowTypeError<JsValue>("The comparison function must be either a function or undefined");

        var typedArray = ValidateTypedArray(thisValue, "toSorted", out var length);
        var result = TypedArrayCreateSameType(function.Realm, typedArray, length);
        if (comparator.IsUndefined)
        {
            var target = result.GetSpan();
            typedArray.ViewedArrayBuffer.GetSpan().Slice(typedArray.ByteOffset, length * typedArray.ElementSize).CopyTo(target);
            SortDefault(result.ElementType, target);
            return result;
        }

        var sorted = SortWithComparator(typedArray, length, comparator.AsObject());
        for (var j = 0; j < sorted.Length; j++)
        {
            result.SetElementFromValue(j, sorted[j]);
        }

        return result;
    }

    /// <summary>SortIndexedProperties(obj, len, SortCompare, read-through-holes) with a comparator (CompareTypedArrayElements): a stable merge sort.</summary>
    private static JsValue[] SortWithComparator(JsTypedArray typedArray, int length, JsObject comparator)
    {
        var items = new JsValue[length];
        for (var k = 0; k < length; k++)
        {
            items[k] = GetElement(typedArray, k);
        }

        MergeSort(items, (x, y) =>
        {
            var v = JsOperations.ToNumber(comparator.Call(JsValue.Undefined, [x, y]));
            return double.IsNaN(v) ? 0 : v;
        });
        return items;
    }

    /// <summary>A stable merge sort; the comparison may throw (a JsException), the values are then left in an unspecified order.</summary>
    private static void MergeSort(JsValue[] items, Func<JsValue, JsValue, double> compare)
    {
        var source = items;
        var buffer = new JsValue[items.Length];
        for (var width = 1; width < items.Length; width *= 2)
        {
            for (var start = 0; start < items.Length; start += 2 * width)
            {
                var middle = Math.Min(start + width, items.Length);
                var end = Math.Min(start + (2 * width), items.Length);
                int left = start, right = middle, output = start;
                while (left < middle && right < end)
                {
                    if (compare(source[right], source[left]) < 0)
                    {
                        buffer[output++] = source[right++];
                    }
                    else
                    {
                        buffer[output++] = source[left++];
                    }
                }

                while (left < middle)
                {
                    buffer[output++] = source[left++];
                }

                while (right < end)
                {
                    buffer[output++] = source[right++];
                }
            }

            (source, buffer) = (buffer, source);
        }

        if (!ReferenceEquals(source, items))
        {
            source.CopyTo(items, 0);
        }
    }

    /// <summary>Sorts the elements of a typed array in place with the default order (CompareTypedArrayElements without comparator: -0 before +0, NaN last).</summary>
    private static void SortDefault(TypedArrayElementType type, Span<byte> bytes)
    {
        var elementSize = BinaryDataOperations.GetElementSize(type);
        var count = bytes.Length / elementSize;
        var littleEndian = BinaryDataOperations.IsLittleEndian;
        switch (type)
        {
            case TypedArrayElementType.Uint8:
            case TypedArrayElementType.Uint8Clamped:
                bytes.Sort();
                break;
            case TypedArrayElementType.BigUint64:
                {
                    var values = new ulong[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = littleEndian ? BinaryPrimitives.ReadUInt64LittleEndian(bytes[(i * 8)..]) : BinaryPrimitives.ReadUInt64BigEndian(bytes[(i * 8)..]);
                    }

                    Array.Sort(values);
                    for (var i = 0; i < count; i++)
                    {
                        if (littleEndian)
                        {
                            BinaryPrimitives.WriteUInt64LittleEndian(bytes[(i * 8)..], values[i]);
                        }
                        else
                        {
                            BinaryPrimitives.WriteUInt64BigEndian(bytes[(i * 8)..], values[i]);
                        }
                    }

                    break;
                }

            case TypedArrayElementType.Float16:
            case TypedArrayElementType.Float32:
            case TypedArrayElementType.Float64:
                {
                    var values = new double[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = BinaryDataOperations.RawBytesToNumeric(type, bytes[(i * elementSize)..], littleEndian).NumberValue;
                    }

                    Array.Sort(values, CompareNumbers);
                    for (var i = 0; i < count; i++)
                    {
                        BinaryDataOperations.NumberToRawBytes(type, values[i], bytes[(i * elementSize)..], littleEndian);
                    }

                    break;
                }

            default:
                {
                    // The signed and unsigned integers up to 64 bits are exactly represented by a long
                    var values = new long[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = type == TypedArrayElementType.BigInt64
                            ? (littleEndian ? BinaryPrimitives.ReadInt64LittleEndian(bytes[(i * 8)..]) : BinaryPrimitives.ReadInt64BigEndian(bytes[(i * 8)..]))
                            : (long)BinaryDataOperations.RawBytesToNumeric(type, bytes[(i * elementSize)..], littleEndian).NumberValue;
                    }

                    Array.Sort(values);
                    for (var i = 0; i < count; i++)
                    {
                        if (type == TypedArrayElementType.BigInt64)
                        {
                            if (littleEndian)
                            {
                                BinaryPrimitives.WriteInt64LittleEndian(bytes[(i * 8)..], values[i]);
                            }
                            else
                            {
                                BinaryPrimitives.WriteInt64BigEndian(bytes[(i * 8)..], values[i]);
                            }
                        }
                        else
                        {
                            BinaryDataOperations.NumberToRawBytes(type, values[i], bytes[(i * elementSize)..], littleEndian);
                        }
                    }

                    break;
                }
        }
    }

    /// <summary>The order of the numbers of CompareTypedArrayElements: -0 before +0, NaN last.</summary>
    private static int CompareNumbers(double x, double y)
    {
        if (double.IsNaN(x))
            return double.IsNaN(y) ? 0 : 1;

        if (double.IsNaN(y))
            return -1;

        if (x < y)
            return -1;

        if (x > y)
            return 1;

        if (x == 0 && y == 0)
            return (double.IsNegative(x) ? 0 : 1) - (double.IsNegative(y) ? 0 : 1);

        return 0;
    }

    /// <summary>%TypedArray%.prototype.subarray(start, end)</summary>
    private static JsValue Subarray(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ThisTypedArray(thisValue, "subarray");
        var buffer = typedArray.ViewedArrayBuffer;
        var sourceLength = typedArray.GetLengthOrZero();
        var startIndex = BinaryDataOperations.ClampRelativeIndex(JsOperations.ToIntegerOrInfinity(Arg(arguments, 0)), sourceLength);
        var elementSize = typedArray.ElementSize;
        var beginByteOffset = typedArray.ByteOffset + (startIndex * elementSize);
        var end = Arg(arguments, 1);
        if (typedArray.IsLengthTracking && end.IsUndefined)
            return TypedArraySpeciesCreate(function.Realm, typedArray, [buffer, (double)beginByteOffset]);

        var endIndex = BinaryDataOperations.ToClampedIndex(end, sourceLength, sourceLength);
        var newLength = Math.Max(endIndex - startIndex, 0);
        return TypedArraySpeciesCreate(function.Realm, typedArray, [buffer, (double)beginByteOffset, (double)newLength]);
    }

    /// <summary>%TypedArray%.prototype.toLocaleString([reserved1 [, reserved2]]) (with the locales and options of ECMA-402)</summary>
    private static JsValue ToLocaleString(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateTypedArray(thisValue, "toLocaleString", out var length);
        var locales = Arg(arguments, 0);
        var options = Arg(arguments, 1);
        var builder = new StringBuilder();
        for (var k = 0; k < length; k++)
        {
            if (k > 0)
            {
                builder.Append(',');
            }

            var element = GetElement(typedArray, k);
            if (!element.IsNullOrUndefined)
            {
                builder.Append(JsOperations.ToString(JsOperations.Invoke(element, ToLocaleStringKey, [locales, options])));
            }

            if (builder.Length > JsString.MaxLength)
                return ThrowHelper.ThrowRangeError<JsValue>("Invalid string length");
        }

        return builder.ToString();
    }

    /// <summary>%TypedArray%.prototype.toReversed()</summary>
    private static JsValue ToReversed(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateTypedArray(thisValue, "toReversed", out var length);
        var result = TypedArrayCreateSameType(function.Realm, typedArray, length);
        var elementSize = typedArray.ElementSize;
        var source = typedArray.ViewedArrayBuffer.GetSpan().Slice(typedArray.ByteOffset, length * elementSize);
        var target = result.GetSpan();
        for (var k = 0; k < length; k++)
        {
            source.Slice((length - k - 1) * elementSize, elementSize).CopyTo(target[(k * elementSize)..]);
        }

        return result;
    }

    /// <summary>%TypedArray%.prototype.with(index, value)</summary>
    private static JsValue With(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateTypedArray(thisValue, "with", out var length);
        var relativeIndex = JsOperations.ToIntegerOrInfinity(Arg(arguments, 0));
        var actualIndex = relativeIndex >= 0 ? relativeIndex : length + relativeIndex;
        var numericValue = BinaryDataOperations.ToNumericForElementType(typedArray.ElementType, Arg(arguments, 1));
        if (!typedArray.IsValidIntegerIndex(actualIndex, out _))
            return ThrowHelper.ThrowRangeError<JsValue>("Invalid typed array index");

        var result = TypedArrayCreateSameType(function.Realm, typedArray, length);
        for (var k = 0; k < length; k++)
        {
            var fromValue = k == actualIndex ? numericValue : GetElement(typedArray, k);
            result.SetElementFromValue(k, fromValue);
        }

        return result;
    }
}
