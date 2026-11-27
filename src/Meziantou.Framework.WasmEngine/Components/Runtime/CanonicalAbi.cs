using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using Meziantou.Framework.WasmEngine.Components.Binary;

namespace Meziantou.Framework.WasmEngine.Components.Runtime;

internal enum LiftLowerDirection
{
    Lift,
    Lower,
}

/// <summary>The context of a conversion between component values and core values (canonical ABI "LiftLowerContext").</summary>
internal sealed class LiftLowerContext(LiftLowerDirection direction, CanonicalOptions options, ComponentInstanceState instance, object? borrowScope = null)
{
    public LiftLowerDirection Direction { get; } = direction;

    public CanonicalOptions Options { get; } = options;

    public ComponentInstanceState Instance { get; } = instance;

    /// <summary>The task (when lowering into a callee) or the subtask (when lifting the arguments of a caller) of the borrowed handles.</summary>
    public object? BorrowScope { get; } = borrowScope;

    public LiftLowerContext ReverseDirection() => new(Direction == LiftLowerDirection.Lift ? LiftLowerDirection.Lower : LiftLowerDirection.Lift, Options, Instance, BorrowScope);

    public WasmMemory Memory => Options.Memory ?? throw ComponentTrap.Create("canonical option `memory` is required");

    public Span<byte> Bytes => Memory.GetSpan();

    public ulong MemoryLength => (ulong)Memory.GetSpan().Length;

    public int PointerSize => Options.PointerSize;

    public ulong Reallocate(ulong old, ulong oldLength, ulong alignment, ulong newLength)
    {
        var realloc = Options.Realloc ?? throw ComponentTrap.Create("canonical option `realloc` is required");
        WasmValue[] arguments = Options.Is64
            ? [WasmValue.FromI64((long)old), WasmValue.FromI64((long)oldLength), WasmValue.FromI64((long)alignment), WasmValue.FromI64((long)newLength)]
            : [WasmValue.FromI32((int)old), WasmValue.FromI32((int)oldLength), WasmValue.FromI32((int)alignment), WasmValue.FromI32((int)newLength)];

        Instance.MayLeave = false;
        WasmValue[] results;
        try
        {
            results = CanonicalAbi.CallCore(realloc, arguments);
        }
        finally
        {
            Instance.MayLeave = true;
        }

        var pointer = Options.Is64 ? (ulong)results[0].I64 : (uint)results[0].I32;
        ComponentTrap.ThrowIf(pointer != CanonicalAbi.AlignTo(pointer, alignment), "realloc return: result not aligned");
        ComponentTrap.ThrowIf(pointer + newLength > MemoryLength || pointer + newLength < pointer, "realloc return: beyond end of memory");
        return pointer;
    }

    public ulong Allocate(ulong alignment, ulong length) => Reallocate(0, 0, alignment, length);
}

/// <summary>The conversions of the canonical ABI (https://github.com/WebAssembly/component-model/blob/main/design/mvp/CanonicalABI.md).</summary>
internal static class CanonicalAbi
{
    public const int MaxFlatParams = 16;
    public const int MaxFlatAsyncParams = 4;
    public const int MaxFlatResults = 1;
    private const ulong MaxStringByteLength = (1UL << 28) - 1;
    private const ulong MaxListByteLength = (1UL << 28) - 1;
    private const uint CanonicalFloat32NaN = 0x7fc00000;
    private const ulong CanonicalFloat64NaN = 0x7ff8000000000000;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly UnicodeEncoding StrictUtf16 = new(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
    private static readonly ConditionalWeakTable<ComponentValueType, ComponentValueType> Despecialized = [];
    private static readonly ConditionalWeakTable<ComponentValueType, StrongBox<(ulong Alignment32, ulong Size32, ulong Alignment64, ulong Size64)>> Layouts = [];

    public static WasmValue[] CallCore(WasmFunction function, WasmValue[] arguments) => function.Call(arguments);

    // ------------------------------------------------------------------------------------------------------------
    // Despecialization and predicates
    // ------------------------------------------------------------------------------------------------------------

    public static ComponentValueType Despecialize(ComponentValueType type)
    {
        if (type.Kind is not (ComponentValueTypeKind.Tuple or ComponentValueTypeKind.Enum or ComponentValueTypeKind.Option or ComponentValueTypeKind.Result or ComponentValueTypeKind.Map))
            return type;

        return Despecialized.GetValue(type, type => type switch
        {
            ComponentTupleType tuple => new ComponentRecordType(tuple.Types.Select((t, i) => new ComponentField(i.ToString(System.Globalization.CultureInfo.InvariantCulture), t))),
            ComponentEnumType @enum => new ComponentVariantType(@enum.Labels.Select(label => new ComponentCase(label, null))),
            ComponentOptionType option => new ComponentVariantType([new("none", null), new("some", option.Type)]),
            ComponentResultType result => new ComponentVariantType([new("ok", result.Ok), new("error", result.Error)]),
            ComponentMapType map => new ComponentListType(Despecialize(new ComponentTupleType([map.KeyType, map.ValueType]))),
            _ => type,
        });
    }

    public static bool ContainsBorrow(ComponentValueType? type) => Contains(type, t => t.Kind == ComponentValueTypeKind.Borrow);

    public static bool ContainsAsyncValue(ComponentValueType? type) => Contains(type, t => t.Kind is ComponentValueTypeKind.Stream or ComponentValueTypeKind.Future);

    public static bool Contains(ComponentValueType? type, Func<ComponentValueType, bool> predicate)
    {
        if (type is null)
            return false;

        type = Despecialize(type);
        return type switch
        {
            ComponentListType list => predicate(type) || Contains(list.ElementType, predicate),
            ComponentAsyncValueType value => predicate(type) || Contains(value.ElementType, predicate),
            ComponentRecordType record => predicate(type) || record.Fields.Any(field => Contains(field.Type, predicate)),
            ComponentVariantType variant => predicate(type) || variant.Cases.Any(@case => Contains(@case.Type, predicate)),
            _ => predicate(type),
        };
    }

    public static bool IsNoneOrNumber(ComponentValueType? type) => type is null || type.Kind is ComponentValueTypeKind.U8 or ComponentValueTypeKind.U16 or ComponentValueTypeKind.U32 or ComponentValueTypeKind.U64 or ComponentValueTypeKind.S8 or ComponentValueTypeKind.S16 or ComponentValueTypeKind.S32 or ComponentValueTypeKind.S64 or ComponentValueTypeKind.F32 or ComponentValueTypeKind.F64;

    // ------------------------------------------------------------------------------------------------------------
    // Alignment and size
    // ------------------------------------------------------------------------------------------------------------

    public static ulong AlignTo(ulong pointer, ulong alignment) => (pointer + alignment - 1) / alignment * alignment;

    private static bool PassRepNotHandle(LiftLowerContext cx, ComponentHandleType borrow) => cx.Instance == borrow.Resource.Implementation && cx.Direction == LiftLowerDirection.Lower;

    public static ulong Alignment(LiftLowerContext cx, ComponentValueType type) => Layout(cx, type).Alignment;

    public static ulong ElemSize(LiftLowerContext cx, ComponentValueType type) => Layout(cx, type).Size;

    private static (ulong Alignment, ulong Size) Layout(LiftLowerContext cx, ComponentValueType type)
    {
        if (ContainsBorrow(type))
            return ComputeLayout(cx, type);

        var layout = Layouts.GetValue(type, t =>
        {
            var (alignment32, size32) = ComputeLayout(null, t, pointerSize: 4);
            var (alignment64, size64) = ComputeLayout(null, t, pointerSize: 8);
            return new StrongBox<(ulong, ulong, ulong, ulong)>((alignment32, size32, alignment64, size64));
        }).Value;

        return cx.Options.Is64 ? (layout.Alignment64, layout.Size64) : (layout.Alignment32, layout.Size32);
    }

    private static (ulong Alignment, ulong Size) ComputeLayout(LiftLowerContext cx, ComponentValueType type) => ComputeLayout(cx, type, (ulong)cx.PointerSize);

    private static (ulong Alignment, ulong Size) ComputeLayout(LiftLowerContext? cx, ComponentValueType type, ulong pointerSize)
    {
        type = Despecialize(type);
        switch (type.Kind)
        {
            case ComponentValueTypeKind.Bool or ComponentValueTypeKind.S8 or ComponentValueTypeKind.U8:
                return (1, 1);
            case ComponentValueTypeKind.S16 or ComponentValueTypeKind.U16:
                return (2, 2);
            case ComponentValueTypeKind.S32 or ComponentValueTypeKind.U32 or ComponentValueTypeKind.F32 or ComponentValueTypeKind.Char or ComponentValueTypeKind.ErrorContext or ComponentValueTypeKind.Own or ComponentValueTypeKind.Stream or ComponentValueTypeKind.Future:
                return (4, 4);
            case ComponentValueTypeKind.S64 or ComponentValueTypeKind.U64 or ComponentValueTypeKind.F64:
                return (8, 8);
            case ComponentValueTypeKind.String:
                return (pointerSize, 2 * pointerSize);
            case ComponentValueTypeKind.Borrow:
                var handle = (ComponentHandleType)type;
                if (cx is not null && PassRepNotHandle(cx, handle))
                {
                    var size = handle.Resource.Is64 ? 8UL : 4UL;
                    return (size, size);
                }

                return (4, 4);
            case ComponentValueTypeKind.List:
                var list = (ComponentListType)type;
                if (list.FixedLength is { } length)
                {
                    var element = ComputeLayout(cx, list.ElementType, pointerSize);
                    return (element.Alignment, (ulong)length * element.Size);
                }

                return (pointerSize, 2 * pointerSize);
            case ComponentValueTypeKind.Record:
            {
                var record = (ComponentRecordType)type;
                ulong alignment = 1;
                ulong size = 0;
                foreach (var field in record.Fields)
                {
                    var layout = ComputeLayout(cx, field.Type, pointerSize);
                    size = AlignTo(size, layout.Alignment) + layout.Size;
                    alignment = Math.Max(alignment, layout.Alignment);
                }

                return (alignment, AlignTo(size, alignment));
            }

            case ComponentValueTypeKind.Variant:
            {
                var variant = (ComponentVariantType)type;
                var discriminant = DiscriminantSize(variant.Cases.Length);
                ulong caseAlignment = 1;
                ulong caseSize = 0;
                foreach (var @case in variant.Cases)
                {
                    if (@case.Type is { } caseType)
                    {
                        var layout = ComputeLayout(cx, caseType, pointerSize);
                        caseAlignment = Math.Max(caseAlignment, layout.Alignment);
                        caseSize = Math.Max(caseSize, layout.Size);
                    }
                }

                var alignment = Math.Max(discriminant, caseAlignment);
                var size = AlignTo(discriminant, caseAlignment) + caseSize;
                return (alignment, AlignTo(size, alignment));
            }

            case ComponentValueTypeKind.Flags:
                var count = ((ComponentFlagsType)type).Labels.Length;
                var flagsSize = count <= 8 ? 1UL : count <= 16 ? 2UL : 4UL;
                return (flagsSize, flagsSize);
            default:
                throw new InvalidOperationException("Unexpected type " + type);
        }
    }

    /// <summary>The size of the discriminant of a variant (canonical ABI "discriminant_type").</summary>
    public static ulong DiscriminantSize(int caseCount) => caseCount <= 256 ? 1UL : caseCount <= 65536 ? 2UL : 4UL;

    private static ulong MaxCaseAlignment(LiftLowerContext cx, ComponentVariantType variant)
    {
        ulong alignment = 1;
        foreach (var @case in variant.Cases)
        {
            if (@case.Type is { } type)
            {
                alignment = Math.Max(alignment, Alignment(cx, type));
            }
        }

        return alignment;
    }

    // ------------------------------------------------------------------------------------------------------------
    // Loading
    // ------------------------------------------------------------------------------------------------------------

    private static ulong LoadInt(LiftLowerContext cx, ulong pointer, int size)
    {
        var bytes = cx.Bytes.Slice((int)pointer, size);
        return size switch
        {
            1 => bytes[0],
            2 => BinaryPrimitives.ReadUInt16LittleEndian(bytes),
            4 => BinaryPrimitives.ReadUInt32LittleEndian(bytes),
            _ => BinaryPrimitives.ReadUInt64LittleEndian(bytes),
        };
    }

    public static ComponentValue Load(LiftLowerContext cx, ulong pointer, ComponentValueType type)
    {
        var despecialized = Despecialize(type);
        switch (despecialized.Kind)
        {
            case ComponentValueTypeKind.Bool:
                return ComponentValue.FromBool(LoadInt(cx, pointer, 1) != 0);
            case ComponentValueTypeKind.U8:
                return ComponentValue.FromU8((byte)LoadInt(cx, pointer, 1));
            case ComponentValueTypeKind.U16:
                return ComponentValue.FromU16((ushort)LoadInt(cx, pointer, 2));
            case ComponentValueTypeKind.U32:
                return ComponentValue.FromU32((uint)LoadInt(cx, pointer, 4));
            case ComponentValueTypeKind.U64:
                return ComponentValue.FromU64(LoadInt(cx, pointer, 8));
            case ComponentValueTypeKind.S8:
                return ComponentValue.FromS8((sbyte)LoadInt(cx, pointer, 1));
            case ComponentValueTypeKind.S16:
                return ComponentValue.FromS16((short)LoadInt(cx, pointer, 2));
            case ComponentValueTypeKind.S32:
                return ComponentValue.FromS32((int)LoadInt(cx, pointer, 4));
            case ComponentValueTypeKind.S64:
                return ComponentValue.FromS64((long)LoadInt(cx, pointer, 8));
            case ComponentValueTypeKind.F32:
                return DecodeF32((uint)LoadInt(cx, pointer, 4));
            case ComponentValueTypeKind.F64:
                return DecodeF64(LoadInt(cx, pointer, 8));
            case ComponentValueTypeKind.Char:
                return ConvertI32ToChar((uint)LoadInt(cx, pointer, 4));
            case ComponentValueTypeKind.String:
                var size = cx.PointerSize;
                return LoadStringFromRange(cx, LoadInt(cx, pointer, size), LoadInt(cx, pointer + (ulong)size, size));
            case ComponentValueTypeKind.ErrorContext:
                return LiftErrorContext(cx, LoadInt(cx, pointer, 4));
            case ComponentValueTypeKind.List:
                var list = (ComponentListType)despecialized;
                if (list.FixedLength is { } length)
                    return LoadListFromValidRange(cx, pointer, (ulong)length, list.ElementType);

                var pointerSize = cx.PointerSize;
                return LoadListFromRange(cx, LoadInt(cx, pointer, pointerSize), LoadInt(cx, pointer + (ulong)pointerSize, pointerSize), list.ElementType);
            case ComponentValueTypeKind.Record:
                return LoadRecord(cx, pointer, (ComponentRecordType)despecialized);
            case ComponentValueTypeKind.Variant:
                return LoadVariant(cx, pointer, (ComponentVariantType)despecialized, type);
            case ComponentValueTypeKind.Flags:
                var flags = (ComponentFlagsType)despecialized;
                return UnpackFlags(LoadInt(cx, pointer, (int)ElemSize(cx, flags)), flags);
            case ComponentValueTypeKind.Own:
                return LiftOwn(cx, LoadInt(cx, pointer, 4), (ComponentOwnType)despecialized);
            case ComponentValueTypeKind.Borrow:
                return LiftBorrow(cx, LoadInt(cx, pointer, 4), (ComponentBorrowType)despecialized);
            case ComponentValueTypeKind.Stream:
                return ComponentStreams.LiftStream(cx, LoadInt(cx, pointer, 4), (ComponentStreamType)despecialized);
            case ComponentValueTypeKind.Future:
                return ComponentStreams.LiftFuture(cx, LoadInt(cx, pointer, 4), (ComponentFutureType)despecialized);
            default:
                throw new InvalidOperationException("Unexpected type " + type);
        }
    }

    private static ComponentValue DecodeF32(uint bits) => ComponentValue.FromF32(BitConverter.Int32BitsToSingle((int)CanonicalizeF32(bits)));

    private static ComponentValue DecodeF64(ulong bits) => ComponentValue.FromF64(BitConverter.Int64BitsToDouble((long)CanonicalizeF64(bits)));

    private static uint CanonicalizeF32(uint bits) => float.IsNaN(BitConverter.Int32BitsToSingle((int)bits)) ? CanonicalFloat32NaN : bits;

    private static ulong CanonicalizeF64(ulong bits) => double.IsNaN(BitConverter.Int64BitsToDouble((long)bits)) ? CanonicalFloat64NaN : bits;

    private static ComponentValue ConvertI32ToChar(uint value)
    {
        ComponentTrap.ThrowIf(value >= 0x110000, "invalid `char` bit pattern");
        ComponentTrap.ThrowIf(value is >= 0xD800 and <= 0xDFFF, "invalid `char` bit pattern");
        return ComponentValue.FromChar(new Rune((int)value));
    }

    private static ulong Utf16Tag(LiftLowerContext cx) => 1UL << (cx.PointerSize * 8 - 1);

    public static ComponentValue LoadStringFromRange(LiftLowerContext cx, ulong pointer, ulong taggedCodeUnits)
    {
        var tag = Utf16Tag(cx);
        ulong alignment;
        ulong byteLength;
        Encoding encoding;
        switch (cx.Options.StringEncoding)
        {
            case ComponentStringEncoding.Utf8:
                alignment = 1;
                byteLength = taggedCodeUnits;
                encoding = StrictUtf8;
                break;

            case ComponentStringEncoding.Utf16:
                alignment = 2;
                byteLength = 2 * taggedCodeUnits;
                encoding = StrictUtf16;
                break;

            default:
                alignment = 2;
                if ((taggedCodeUnits & tag) != 0)
                {
                    byteLength = 2 * (taggedCodeUnits ^ tag);
                    encoding = StrictUtf16;
                }
                else
                {
                    byteLength = taggedCodeUnits;
                    encoding = Encoding.Latin1;
                }

                break;
        }

        ComponentTrap.ThrowIf(byteLength > MaxStringByteLength, "string content out-of-bounds");
        ComponentTrap.ThrowIf(pointer != AlignTo(pointer, alignment), "unaligned pointer");
        ComponentTrap.ThrowIf(pointer + byteLength > cx.MemoryLength || pointer > cx.MemoryLength, "string pointer/length out of bounds of memory");
        string value;
        try
        {
            value = encoding.GetString(cx.Bytes.Slice((int)pointer, (int)byteLength));
        }
        catch (DecoderFallbackException)
        {
            throw ComponentTrap.Create("invalid string encoding");
        }

        if (ReferenceEquals(encoding, StrictUtf16) && (byteLength & 1) != 0)
            throw ComponentTrap.Create("invalid string encoding");

        var isUtf16 = ReferenceEquals(encoding, StrictUtf16);
        var codeUnits = cx.Options.StringEncoding == ComponentStringEncoding.Latin1Utf16 && isUtf16 ? taggedCodeUnits ^ tag : taggedCodeUnits;
        return ComponentValue.FromString(new ComponentString(value, cx.Options.StringEncoding, codeUnits, isUtf16));
    }

    private static ComponentValue LiftErrorContext(LiftLowerContext cx, ulong index)
    {
        var handle = cx.Instance.Handles.Get((long)index);
        return handle is ErrorContextHandle errorContext ? ComponentValue.FromErrorContext(errorContext.ErrorContext) : throw ComponentTrap.Create("handle is not an error-context");
    }

    public static ComponentValue LoadListFromRange(LiftLowerContext cx, ulong pointer, ulong length, ComponentValueType elementType)
    {
        var size = ElemSize(cx, elementType);
        ComponentTrap.ThrowIf(length > MaxListByteLength || length * size > MaxListByteLength, "list content out-of-bounds");
        ComponentTrap.ThrowIf(pointer != AlignTo(pointer, Alignment(cx, elementType)), "list pointer is not aligned");
        ComponentTrap.ThrowIf(pointer + length * size > cx.MemoryLength || pointer > cx.MemoryLength, "list pointer/length out of bounds of memory");
        return LoadListFromValidRange(cx, pointer, length, elementType);
    }

    public static ComponentValue LoadListFromValidRange(LiftLowerContext cx, ulong pointer, ulong length, ComponentValueType elementType)
    {
        if (elementType.Kind == ComponentValueTypeKind.U8)
            return ComponentValue.FromByteArray(cx.Bytes.Slice((int)pointer, (int)length).ToArray());

        var size = ElemSize(cx, elementType);
        var values = new ComponentValue[length];
        for (ulong i = 0; i < length; i++)
        {
            values[i] = Load(cx, pointer + i * size, elementType);
        }

        return ComponentValue.FromListArray(values);
    }

    private static ComponentValue LoadRecord(LiftLowerContext cx, ulong pointer, ComponentRecordType record)
    {
        var fields = new ComponentValue[record.Fields.Length];
        for (var i = 0; i < fields.Length; i++)
        {
            var type = record.Fields[i].Type;
            pointer = AlignTo(pointer, Alignment(cx, type));
            fields[i] = Load(cx, pointer, type);
            pointer += ElemSize(cx, type);
        }

        return ComponentValue.FromRecord(fields);
    }

    private static ComponentValue LoadVariant(LiftLowerContext cx, ulong pointer, ComponentVariantType variant, ComponentValueType originalType)
    {
        var discriminantSize = DiscriminantSize(variant.Cases.Length);
        var index = LoadInt(cx, pointer, (int)discriminantSize);
        pointer += discriminantSize;
        ComponentTrap.ThrowIf(index >= (ulong)variant.Cases.Length, originalType.Kind switch
        {
            ComponentValueTypeKind.Option => "invalid option discriminant",
            ComponentValueTypeKind.Result => "invalid result discriminant",
            ComponentValueTypeKind.Enum => "invalid enum discriminant",
            _ => "invalid variant discriminant",
        });

        var @case = variant.Cases[(int)index];
        pointer = AlignTo(pointer, MaxCaseAlignment(cx, variant));
        return @case.Type is { } type ? ComponentValue.FromCase(@case.Name, Load(cx, pointer, type)) : ComponentValue.FromCase(@case.Name);
    }

    private static ComponentValue UnpackFlags(ulong value, ComponentFlagsType flags)
    {
        var labels = new List<string>();
        for (var i = 0; i < flags.Labels.Length; i++)
        {
            if ((value & (1UL << i)) != 0)
            {
                labels.Add(flags.Labels[i]);
            }
        }

        return ComponentValue.FromFlags(labels);
    }

    private static ComponentValue LiftOwn(LiftLowerContext cx, ulong index, ComponentOwnType type)
    {
        var item = cx.Instance.Handles.Remove((long)index);
        if (item is not ResourceHandle handle)
            throw ComponentTrap.Create("handle index is not a resource");

        ComponentTrap.ThrowIf(!ReferenceEquals(handle.Type, type.Resource), "handle index used with the wrong type");
        ComponentTrap.ThrowIf(handle.LendCount != 0, "cannot remove owned resource while borrowed");
        ComponentTrap.ThrowIf(!handle.Own, "cannot lift own from a borrow");
        return ComponentValue.FromResource(handle.Resource);
    }

    private static ComponentValue LiftBorrow(LiftLowerContext cx, ulong index, ComponentBorrowType type)
    {
        var item = cx.Instance.Handles.Get((long)index);
        if (item is not ResourceHandle handle)
            throw ComponentTrap.Create("handle index is not a resource");

        ComponentTrap.ThrowIf(!ReferenceEquals(handle.Type, type.Resource), "handle index used with the wrong type");
        (cx.BorrowScope as Subtask)?.AddLender(handle);
        return ComponentValue.FromResource(handle.Resource);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Storing
    // ------------------------------------------------------------------------------------------------------------

    private static void StoreInt(LiftLowerContext cx, ulong value, ulong pointer, int size)
    {
        var bytes = cx.Bytes.Slice((int)pointer, size);
        switch (size)
        {
            case 1:
                bytes[0] = (byte)value;
                break;
            case 2:
                BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value);
                break;
            case 4:
                BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)value);
                break;
            default:
                BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
                break;
        }
    }

    private static void Expect(ComponentValue value, ComponentValueKind kind, ComponentValueType type)
    {
        if (value.Kind != kind)
            throw new WasmTypeMismatchException("The value " + value + " is not a " + type);
    }

    public static void Store(LiftLowerContext cx, ComponentValue value, ComponentValueType type, ulong pointer)
    {
        var despecialized = Despecialize(type);
        switch (despecialized.Kind)
        {
            case ComponentValueTypeKind.Bool:
                Expect(value, ComponentValueKind.Bool, type);
                StoreInt(cx, value.Bits, pointer, 1);
                break;
            case ComponentValueTypeKind.U8 or ComponentValueTypeKind.S8:
                ExpectNumber(value, despecialized);
                StoreInt(cx, value.Bits, pointer, 1);
                break;
            case ComponentValueTypeKind.U16 or ComponentValueTypeKind.S16:
                ExpectNumber(value, despecialized);
                StoreInt(cx, value.Bits, pointer, 2);
                break;
            case ComponentValueTypeKind.U32 or ComponentValueTypeKind.S32:
                ExpectNumber(value, despecialized);
                StoreInt(cx, value.Bits, pointer, 4);
                break;
            case ComponentValueTypeKind.U64 or ComponentValueTypeKind.S64:
                ExpectNumber(value, despecialized);
                StoreInt(cx, value.Bits, pointer, 8);
                break;
            case ComponentValueTypeKind.F32:
                Expect(value, ComponentValueKind.F32, type);
                StoreInt(cx, CanonicalizeF32((uint)value.Bits), pointer, 4);
                break;
            case ComponentValueTypeKind.F64:
                Expect(value, ComponentValueKind.F64, type);
                StoreInt(cx, CanonicalizeF64(value.Bits), pointer, 8);
                break;
            case ComponentValueTypeKind.Char:
                Expect(value, ComponentValueKind.Char, type);
                StoreInt(cx, value.Bits, pointer, 4);
                break;
            case ComponentValueTypeKind.String:
                Expect(value, ComponentValueKind.String, type);
                var (begin, taggedCodeUnits) = StoreStringIntoRange(cx, value);
                StoreInt(cx, begin, pointer, cx.PointerSize);
                StoreInt(cx, taggedCodeUnits, pointer + (ulong)cx.PointerSize, cx.PointerSize);
                break;
            case ComponentValueTypeKind.ErrorContext:
                Expect(value, ComponentValueKind.ErrorContext, type);
                StoreInt(cx, (ulong)cx.Instance.Handles.Add(new ErrorContextHandle(value.AsErrorContext())), pointer, 4);
                break;
            case ComponentValueTypeKind.List:
                Expect(value, ComponentValueKind.List, type);
                var list = (ComponentListType)despecialized;
                if (list.FixedLength is { } length)
                {
                    if (value.ListLength != length)
                        throw new WasmTypeMismatchException("The list " + value + " does not have " + length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " elements");

                    StoreListIntoValidRange(cx, value, pointer, list.ElementType);
                }
                else
                {
                    var (listPointer, listLength) = StoreListIntoRange(cx, value, list.ElementType);
                    StoreInt(cx, listPointer, pointer, cx.PointerSize);
                    StoreInt(cx, listLength, pointer + (ulong)cx.PointerSize, cx.PointerSize);
                }

                break;
            case ComponentValueTypeKind.Record:
                Expect(value, ComponentValueKind.Record, type);
                var record = (ComponentRecordType)despecialized;
                var fields = value.RecordFields;
                if (fields.Length != record.Fields.Length)
                    throw new WasmTypeMismatchException("The value " + value + " does not have the fields of " + type);

                for (var i = 0; i < fields.Length; i++)
                {
                    var fieldType = record.Fields[i].Type;
                    pointer = AlignTo(pointer, Alignment(cx, fieldType));
                    Store(cx, fields[i], fieldType, pointer);
                    pointer += ElemSize(cx, fieldType);
                }

                break;
            case ComponentValueTypeKind.Variant:
                var variant = (ComponentVariantType)despecialized;
                var (caseIndex, caseValue) = MatchCase(value, variant, type);
                var discriminantSize = DiscriminantSize(variant.Cases.Length);
                StoreInt(cx, (ulong)caseIndex, pointer, (int)discriminantSize);
                pointer = AlignTo(pointer + discriminantSize, MaxCaseAlignment(cx, variant));
                if (variant.Cases[caseIndex].Type is { } caseType)
                {
                    Store(cx, caseValue!.Value, caseType, pointer);
                }

                break;
            case ComponentValueTypeKind.Flags:
                var flags = (ComponentFlagsType)despecialized;
                StoreInt(cx, PackFlags(value, flags), pointer, (int)ElemSize(cx, flags));
                break;
            case ComponentValueTypeKind.Own:
                StoreInt(cx, (ulong)AddOwn(cx, value, (ComponentOwnType)despecialized), pointer, 4);
                break;
            case ComponentValueTypeKind.Borrow:
                var borrow = (ComponentBorrowType)despecialized;
                if (PassRepNotHandle(cx, borrow))
                {
                    StoreInt(cx, (ulong)GetResource(value, borrow).GuestRepresentation, pointer, borrow.Resource.Is64 ? 8 : 4);
                }
                else
                {
                    StoreInt(cx, (ulong)AddBorrow(cx, value, borrow), pointer, 4);
                }

                break;
            case ComponentValueTypeKind.Stream:
                StoreInt(cx, ComponentStreams.LowerStream(cx, value, (ComponentStreamType)despecialized), pointer, 4);
                break;
            case ComponentValueTypeKind.Future:
                StoreInt(cx, ComponentStreams.LowerFuture(cx, value, (ComponentFutureType)despecialized), pointer, 4);
                break;
            default:
                throw new InvalidOperationException("Unexpected type " + type);
        }
    }

    private static void ExpectNumber(ComponentValue value, ComponentValueType type)
    {
        var expected = type.Kind switch
        {
            ComponentValueTypeKind.U8 => ComponentValueKind.U8,
            ComponentValueTypeKind.S8 => ComponentValueKind.S8,
            ComponentValueTypeKind.U16 => ComponentValueKind.U16,
            ComponentValueTypeKind.S16 => ComponentValueKind.S16,
            ComponentValueTypeKind.U32 => ComponentValueKind.U32,
            ComponentValueTypeKind.S32 => ComponentValueKind.S32,
            ComponentValueTypeKind.U64 => ComponentValueKind.U64,
            _ => ComponentValueKind.S64,
        };

        Expect(value, expected, type);
    }

    private static (int Index, ComponentValue? Payload) MatchCase(ComponentValue value, ComponentVariantType variant, ComponentValueType type)
    {
        Expect(value, ComponentValueKind.Case, type);
        var name = value.CaseName;
        for (var i = 0; i < variant.Cases.Length; i++)
        {
            if (string.Equals(variant.Cases[i].Name, name, StringComparison.Ordinal))
            {
                if ((variant.Cases[i].Type is null) != (value.Payload is null))
                    throw new WasmTypeMismatchException("The payload of the case '" + name + "' does not match the type " + type);

                return (i, value.Payload);
            }
        }

        throw new WasmTypeMismatchException("The case '" + name + "' is not a case of " + type);
    }

    private static ulong PackFlags(ComponentValue value, ComponentFlagsType flags)
    {
        Expect(value, ComponentValueKind.Flags, flags);
        ulong result = 0;
        foreach (var label in value.AsFlags())
        {
            var index = flags.Labels.IndexOf(label, 0, StringComparer.Ordinal);
            if (index < 0)
                throw new WasmTypeMismatchException("The label '" + label + "' is not a label of " + flags);

            result |= 1UL << index;
        }

        return result;
    }

    private static ComponentResource GetResource(ComponentValue value, ComponentHandleType type)
    {
        Expect(value, ComponentValueKind.Resource, type);
        var resource = value.AsResource();
        if (!ReferenceEquals(resource.Type, type.Resource))
            throw ComponentTrap.Create("handle index used with the wrong type");

        return resource;
    }

    private static int AddOwn(LiftLowerContext cx, ComponentValue value, ComponentOwnType type) => cx.Instance.Handles.Add(new ResourceHandle(GetResource(value, type), own: true, borrowScope: null));

    private static int AddBorrow(LiftLowerContext cx, ComponentValue value, ComponentBorrowType type)
    {
        var scope = cx.BorrowScope as ComponentTask;
        var handle = new ResourceHandle(GetResource(value, type), own: false, scope);
        if (scope is not null)
        {
            scope.BorrowCount++;
        }

        return cx.Instance.Handles.Add(handle);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Strings
    // ------------------------------------------------------------------------------------------------------------

    private enum SimpleEncoding
    {
        Utf8,
        Utf16,
        Latin1,
    }

    public static (ulong Pointer, ulong TaggedCodeUnits) StoreStringIntoRange(LiftLowerContext cx, ComponentValue value)
    {
        // A string of the host is UTF-16: its code units are its chars
        var (source, sourceEncoding, codeUnits, isUtf16) = value.Object is ComponentString lifted
            ? (lifted.Value, lifted.Encoding, lifted.CodeUnits, lifted.IsUtf16)
            : (value.AsString(), ComponentStringEncoding.Utf16, (ulong)value.AsString().Length, true);

        ValidateScalarValues(source);
        var simpleEncoding = sourceEncoding switch
        {
            ComponentStringEncoding.Utf8 => SimpleEncoding.Utf8,
            ComponentStringEncoding.Utf16 => SimpleEncoding.Utf16,
            _ => isUtf16 ? SimpleEncoding.Utf16 : SimpleEncoding.Latin1,
        };

        switch (cx.Options.StringEncoding)
        {
            case ComponentStringEncoding.Utf8:
                return simpleEncoding switch
                {
                    SimpleEncoding.Utf8 => StoreStringCopy(cx, source, codeUnits, 1, 1, StrictUtf8),
                    SimpleEncoding.Utf16 => StoreStringToUtf8(cx, source, codeUnits, codeUnits * 3),
                    _ => StoreStringToUtf8(cx, source, codeUnits, codeUnits * 2),
                };

            case ComponentStringEncoding.Utf16:
                return simpleEncoding switch
                {
                    SimpleEncoding.Utf8 => StoreUtf8ToUtf16(cx, source, codeUnits),
                    _ => StoreStringCopy(cx, source, codeUnits, 2, 2, StrictUtf16),
                };

            default:
                if (sourceEncoding != ComponentStringEncoding.Latin1Utf16)
                    return StoreStringToLatin1OrUtf16(cx, source, codeUnits);

                return simpleEncoding == SimpleEncoding.Latin1 ? StoreStringCopy(cx, source, codeUnits, 1, 2, Encoding.Latin1) : StoreProbablyUtf16ToLatin1OrUtf16(cx, source, codeUnits);
        }
    }

    private static void ValidateScalarValues(string value)
    {
        if (value.AsSpan().IndexOfAnyInRange('\uD800', '\uDFFF') < 0)
            return;

        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsSurrogate(value[i]))
            {
                if (!char.IsHighSurrogate(value[i]) || i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                    throw new WasmTypeMismatchException("The string contains an unpaired surrogate, which is not a Unicode scalar value");

                i++;
            }
        }
    }

    private static void Write(LiftLowerContext cx, ulong pointer, ReadOnlySpan<byte> bytes)
    {
        ComponentTrap.ThrowIf(pointer + (ulong)bytes.Length > cx.MemoryLength, "string pointer/length out of bounds of memory");
        bytes.CopyTo(cx.Bytes[(int)pointer..]);
    }

    private static (ulong, ulong) StoreStringCopy(LiftLowerContext cx, string source, ulong codeUnits, ulong destinationCodeUnitSize, ulong destinationAlignment, Encoding destinationEncoding)
    {
        var byteLength = destinationCodeUnitSize * codeUnits;
        ComponentTrap.ThrowIf(byteLength > MaxStringByteLength, "string content out-of-bounds");
        var pointer = cx.Allocate(destinationAlignment, byteLength);
        var encoded = destinationEncoding.GetBytes(source);
        Write(cx, pointer, encoded);
        return (pointer, codeUnits);
    }

    private static (ulong, ulong) StoreStringToUtf8(LiftLowerContext cx, string source, ulong codeUnits, ulong worstCaseSize)
    {
        ComponentTrap.ThrowIf(codeUnits > MaxStringByteLength, "string content out-of-bounds");
        var pointer = cx.Allocate(1, codeUnits);
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] < 0x80)
            {
                Write(cx, pointer + (ulong)i, [(byte)source[i]]);
                continue;
            }

            ComponentTrap.ThrowIf(worstCaseSize > MaxStringByteLength * 3, "string content out-of-bounds");
            pointer = cx.Reallocate(pointer, codeUnits, 1, worstCaseSize);
            var encoded = StrictUtf8.GetBytes(source);
            Write(cx, pointer + (ulong)i, encoded.AsSpan(i));
            if (worstCaseSize > (ulong)encoded.Length)
            {
                pointer = cx.Reallocate(pointer, worstCaseSize, 1, (ulong)encoded.Length);
            }

            return (pointer, (ulong)encoded.Length);
        }

        return (pointer, codeUnits);
    }

    private static (ulong, ulong) StoreUtf8ToUtf16(LiftLowerContext cx, string source, ulong codeUnits)
    {
        var worstCaseSize = 2 * codeUnits;
        ComponentTrap.ThrowIf(worstCaseSize > MaxStringByteLength * 2, "string content out-of-bounds");
        var pointer = cx.Allocate(2, worstCaseSize);
        var encoded = StrictUtf16.GetBytes(source);
        Write(cx, pointer, encoded);
        if ((ulong)encoded.Length < worstCaseSize)
        {
            pointer = cx.Reallocate(pointer, worstCaseSize, 2, (ulong)encoded.Length);
        }

        return (pointer, (ulong)encoded.Length / 2);
    }

    private static (ulong, ulong) StoreStringToLatin1OrUtf16(LiftLowerContext cx, string source, ulong codeUnits)
    {
        ComponentTrap.ThrowIf(codeUnits > MaxStringByteLength, "string content out-of-bounds");
        var pointer = cx.Allocate(2, codeUnits);
        ulong latin1Length = 0;
        foreach (var rune in source.EnumerateRunes())
        {
            if (rune.Value < 0x100)
            {
                Write(cx, pointer + latin1Length, [(byte)rune.Value]);
                latin1Length++;
                continue;
            }

            var worstCaseSize = 2 * codeUnits;
            pointer = cx.Reallocate(pointer, codeUnits, 2, worstCaseSize);
            var bytes = cx.Bytes;
            for (var j = (long)latin1Length - 1; j >= 0; j--)
            {
                bytes[(int)pointer + (2 * (int)j)] = bytes[(int)pointer + (int)j];
                bytes[(int)pointer + (2 * (int)j) + 1] = 0;
            }

            var encoded = StrictUtf16.GetBytes(source);
            Write(cx, pointer + (2 * latin1Length), encoded.AsSpan((int)(2 * latin1Length)));
            if (worstCaseSize > (ulong)encoded.Length)
            {
                pointer = cx.Reallocate(pointer, worstCaseSize, 2, (ulong)encoded.Length);
            }

            return (pointer, ((ulong)encoded.Length / 2) | Utf16Tag(cx));
        }

        if (latin1Length < codeUnits)
        {
            pointer = cx.Reallocate(pointer, codeUnits, 2, latin1Length);
        }

        return (pointer, latin1Length);
    }

    private static (ulong, ulong) StoreProbablyUtf16ToLatin1OrUtf16(LiftLowerContext cx, string source, ulong codeUnits)
    {
        var sourceByteLength = 2 * codeUnits;
        ComponentTrap.ThrowIf(sourceByteLength > MaxStringByteLength * 2, "string content out-of-bounds");
        var pointer = cx.Allocate(2, sourceByteLength);
        var encoded = StrictUtf16.GetBytes(source);
        Write(cx, pointer, encoded);
        if (source.AsSpan().ContainsAnyExceptInRange('\0', 'ÿ'))
            return (pointer, ((ulong)encoded.Length / 2) | Utf16Tag(cx));

        var latin1Size = (ulong)encoded.Length / 2;
        var bytes = cx.Bytes;
        for (var i = 0; i < (int)latin1Size; i++)
        {
            bytes[(int)pointer + i] = bytes[(int)pointer + (2 * i)];
        }

        pointer = cx.Reallocate(pointer, sourceByteLength, 2, latin1Size);
        return (pointer, latin1Size);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Lists
    // ------------------------------------------------------------------------------------------------------------

    public static (ulong Pointer, ulong Length) StoreListIntoRange(LiftLowerContext cx, ComponentValue value, ComponentValueType elementType)
    {
        var length = (ulong)value.ListLength;
        var byteLength = length * ElemSize(cx, elementType);
        ComponentTrap.ThrowIf(byteLength > MaxListByteLength, "list content out-of-bounds");
        var pointer = cx.Allocate(Alignment(cx, elementType), byteLength);
        StoreListIntoValidRange(cx, value, pointer, elementType);
        return (pointer, length);
    }

    public static void StoreListIntoValidRange(LiftLowerContext cx, ComponentValue value, ulong pointer, ComponentValueType elementType)
    {
        var length = value.ListLength;
        if (elementType.Kind == ComponentValueTypeKind.U8 && value.ByteArray is { } bytes)
        {
            ComponentTrap.ThrowIf(pointer + (ulong)bytes.Length > cx.MemoryLength, "list pointer/length out of bounds of memory");
            bytes.CopyTo(cx.Bytes[(int)pointer..]);
            return;
        }

        var size = ElemSize(cx, elementType);
        for (var i = 0; i < length; i++)
        {
            Store(cx, value.GetListElement(i), elementType, pointer + ((ulong)i * size));
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Flattening
    // ------------------------------------------------------------------------------------------------------------

    public static (CoreValKind[] Parameters, CoreValKind[] Results) FlattenFunctionType(LiftLowerContext cx, ComponentFunctionType type)
    {
        var pointerType = cx.Options.Is64 ? CoreValKind.I64 : CoreValKind.I32;
        var parameters = FlattenTypes(cx.ReverseDirection(), type.ParameterTypes);
        var results = FlattenTypes(cx, type.ResultTypes);
        if (!cx.Options.Async)
        {
            if (parameters.Count > MaxFlatParams)
            {
                parameters = [pointerType];
            }

            if (results.Count > MaxFlatResults)
            {
                if (cx.Direction == LiftLowerDirection.Lift)
                {
                    results = [pointerType];
                }
                else
                {
                    parameters.Add(pointerType);
                    results = [];
                }
            }

            return ([.. parameters], [.. results]);
        }

        if (cx.Direction == LiftLowerDirection.Lift)
        {
            if (parameters.Count > MaxFlatParams)
            {
                parameters = [pointerType];
            }

            return ([.. parameters], cx.Options.Callback is not null ? [CoreValKind.I32] : []);
        }

        if (parameters.Count > MaxFlatAsyncParams)
        {
            parameters = [pointerType];
        }

        if (results.Count > 0)
        {
            parameters.Add(pointerType);
        }

        return ([.. parameters], [CoreValKind.I32]);
    }

    /// <summary>Flattens types without memory (validation): the pointers are <c>i32</c> or <c>i64</c>.</summary>
    public static List<CoreValKind> FlattenTypes(LiftLowerContext cx, IEnumerable<ComponentValueType> types)
    {
        var result = new List<CoreValKind>();
        foreach (var type in types)
        {
            FlattenType(cx, type, result);
        }

        return result;
    }

    public static void FlattenType(LiftLowerContext cx, ComponentValueType type, List<CoreValKind> result)
    {
        var pointerType = cx.Options.Is64 ? CoreValKind.I64 : CoreValKind.I32;
        var despecialized = Despecialize(type);
        switch (despecialized.Kind)
        {
            case ComponentValueTypeKind.S64 or ComponentValueTypeKind.U64:
                result.Add(CoreValKind.I64);
                break;
            case ComponentValueTypeKind.F32:
                result.Add(CoreValKind.F32);
                break;
            case ComponentValueTypeKind.F64:
                result.Add(CoreValKind.F64);
                break;
            case ComponentValueTypeKind.String:
                result.Add(pointerType);
                result.Add(pointerType);
                break;
            case ComponentValueTypeKind.List:
                var list = (ComponentListType)despecialized;
                if (list.FixedLength is { } length)
                {
                    for (var i = 0; i < length; i++)
                    {
                        FlattenType(cx, list.ElementType, result);
                    }
                }
                else
                {
                    result.Add(pointerType);
                    result.Add(pointerType);
                }

                break;
            case ComponentValueTypeKind.Record:
                foreach (var field in ((ComponentRecordType)despecialized).Fields)
                {
                    FlattenType(cx, field.Type, result);
                }

                break;
            case ComponentValueTypeKind.Variant:
                result.AddRange(FlattenVariant(cx, (ComponentVariantType)despecialized));
                break;
            case ComponentValueTypeKind.Borrow:
                var borrow = (ComponentBorrowType)despecialized;
                result.Add(PassRepNotHandle(cx, borrow) && borrow.Resource.Is64 ? CoreValKind.I64 : CoreValKind.I32);
                break;
            default:
                result.Add(CoreValKind.I32);
                break;
        }
    }

    private static List<CoreValKind> FlattenVariant(LiftLowerContext cx, ComponentVariantType variant)
    {
        var flat = new List<CoreValKind>();
        foreach (var @case in variant.Cases)
        {
            if (@case.Type is null)
                continue;

            var caseFlat = new List<CoreValKind>();
            FlattenType(cx, @case.Type, caseFlat);
            for (var i = 0; i < caseFlat.Count; i++)
            {
                if (i < flat.Count)
                {
                    flat[i] = Join(flat[i], caseFlat[i]);
                }
                else
                {
                    flat.Add(caseFlat[i]);
                }
            }
        }

        flat.Insert(0, CoreValKind.I32);
        return flat;
    }

    private static CoreValKind Join(CoreValKind a, CoreValKind b)
    {
        if (a == b)
            return a;

        if ((a == CoreValKind.I32 && b == CoreValKind.F32) || (a == CoreValKind.F32 && b == CoreValKind.I32))
            return CoreValKind.I32;

        return CoreValKind.I64;
    }

    // ------------------------------------------------------------------------------------------------------------
    // Flat lifting
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>An iterator of core values (canonical ABI "CoreValueIter").</summary>
    public sealed class CoreValueIterator(IReadOnlyList<WasmValue> values)
    {
        private int _index;

        public WasmValue Next() => values[_index++];

        public ulong NextPointer(LiftLowerContext cx)
        {
            var value = Next();
            return cx.Options.Is64 ? (ulong)value.I64 : (uint)value.I32;
        }
    }

    public static ComponentValue LiftFlat(LiftLowerContext cx, CoreValueIterator values, ComponentValueType type)
    {
        var despecialized = Despecialize(type);
        switch (despecialized.Kind)
        {
            case ComponentValueTypeKind.Bool:
                return ComponentValue.FromBool(values.Next().I32 != 0);
            case ComponentValueTypeKind.U8:
                return ComponentValue.FromU8((byte)values.Next().I32);
            case ComponentValueTypeKind.U16:
                return ComponentValue.FromU16((ushort)values.Next().I32);
            case ComponentValueTypeKind.U32:
                return ComponentValue.FromU32((uint)values.Next().I32);
            case ComponentValueTypeKind.U64:
                return ComponentValue.FromU64((ulong)values.Next().I64);
            case ComponentValueTypeKind.S8:
                return ComponentValue.FromS8((sbyte)values.Next().I32);
            case ComponentValueTypeKind.S16:
                return ComponentValue.FromS16((short)values.Next().I32);
            case ComponentValueTypeKind.S32:
                return ComponentValue.FromS32(values.Next().I32);
            case ComponentValueTypeKind.S64:
                return ComponentValue.FromS64(values.Next().I64);
            case ComponentValueTypeKind.F32:
                return DecodeF32((uint)values.Next().F32Bits);
            case ComponentValueTypeKind.F64:
                return DecodeF64((ulong)values.Next().F64Bits);
            case ComponentValueTypeKind.Char:
                return ConvertI32ToChar((uint)values.Next().I32);
            case ComponentValueTypeKind.String:
                var pointer = values.NextPointer(cx);
                return LoadStringFromRange(cx, pointer, values.NextPointer(cx));
            case ComponentValueTypeKind.ErrorContext:
                return LiftErrorContext(cx, (uint)values.Next().I32);
            case ComponentValueTypeKind.List:
                var list = (ComponentListType)despecialized;
                if (list.FixedLength is { } length)
                {
                    var elements = new ComponentValue[length];
                    for (var i = 0; i < length; i++)
                    {
                        elements[i] = LiftFlat(cx, values, list.ElementType);
                    }

                    return ComponentValue.FromListArray(elements);
                }

                var listPointer = values.NextPointer(cx);
                return LoadListFromRange(cx, listPointer, values.NextPointer(cx), list.ElementType);
            case ComponentValueTypeKind.Record:
                var record = (ComponentRecordType)despecialized;
                var fields = new ComponentValue[record.Fields.Length];
                for (var i = 0; i < fields.Length; i++)
                {
                    fields[i] = LiftFlat(cx, values, record.Fields[i].Type);
                }

                return ComponentValue.FromRecord(fields);
            case ComponentValueTypeKind.Variant:
                return LiftFlatVariant(cx, values, (ComponentVariantType)despecialized, type);
            case ComponentValueTypeKind.Flags:
                return UnpackFlags((uint)values.Next().I32, (ComponentFlagsType)despecialized);
            case ComponentValueTypeKind.Own:
                return LiftOwn(cx, (uint)values.Next().I32, (ComponentOwnType)despecialized);
            case ComponentValueTypeKind.Borrow:
                return LiftBorrow(cx, (uint)values.Next().I32, (ComponentBorrowType)despecialized);
            case ComponentValueTypeKind.Stream:
                return ComponentStreams.LiftStream(cx, (uint)values.Next().I32, (ComponentStreamType)despecialized);
            case ComponentValueTypeKind.Future:
                return ComponentStreams.LiftFuture(cx, (uint)values.Next().I32, (ComponentFutureType)despecialized);
            default:
                throw new InvalidOperationException("Unexpected type " + type);
        }
    }

    private static ComponentValue LiftFlatVariant(LiftLowerContext cx, CoreValueIterator values, ComponentVariantType variant, ComponentValueType originalType)
    {
        var flatTypes = FlattenVariant(cx, variant);
        flatTypes.RemoveAt(0);
        var caseIndex = (uint)values.Next().I32;
        ComponentTrap.ThrowIf(caseIndex >= (uint)variant.Cases.Length, originalType.Kind switch
        {
            ComponentValueTypeKind.Option => "invalid option discriminant",
            ComponentValueTypeKind.Result => "invalid result discriminant",
            ComponentValueTypeKind.Enum => "invalid enum discriminant",
            _ => "invalid variant discriminant",
        });

        var @case = variant.Cases[(int)caseIndex];
        var consumed = 0;
        ComponentValue? payload = null;
        if (@case.Type is { } caseType)
        {
            var caseFlat = new List<CoreValKind>();
            FlattenType(cx, caseType, caseFlat);
            var coerced = new WasmValue[caseFlat.Count];
            for (var i = 0; i < caseFlat.Count; i++)
            {
                var have = flatTypes[i];
                var want = caseFlat[i];
                var value = values.Next();
                coerced[i] = (have, want) switch
                {
                    (CoreValKind.I32, CoreValKind.F32) => WasmValue.FromF32Bits(value.I32),
                    (CoreValKind.I64, CoreValKind.I32) => WasmValue.FromI32((int)value.I64),
                    (CoreValKind.I64, CoreValKind.F32) => WasmValue.FromF32Bits((int)value.I64),
                    (CoreValKind.I64, CoreValKind.F64) => WasmValue.FromF64Bits(value.I64),
                    _ => value,
                };
            }

            consumed = caseFlat.Count;
            payload = LiftFlat(cx, new CoreValueIterator(coerced), caseType);
        }

        for (var i = consumed; i < flatTypes.Count; i++)
        {
            _ = values.Next();
        }

        return ComponentValue.FromCase(@case.Name, payload);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Flat lowering
    // ------------------------------------------------------------------------------------------------------------

    public static void LowerFlat(LiftLowerContext cx, ComponentValue value, ComponentValueType type, List<WasmValue> result)
    {
        var despecialized = Despecialize(type);
        switch (despecialized.Kind)
        {
            case ComponentValueTypeKind.Bool:
                Expect(value, ComponentValueKind.Bool, type);
                result.Add(WasmValue.FromI32((int)value.Bits));
                break;
            case ComponentValueTypeKind.U8 or ComponentValueTypeKind.U16 or ComponentValueTypeKind.U32 or ComponentValueTypeKind.S8 or ComponentValueTypeKind.S16 or ComponentValueTypeKind.S32:
                ExpectNumber(value, despecialized);
                result.Add(WasmValue.FromI32((int)value.Bits));
                break;
            case ComponentValueTypeKind.U64 or ComponentValueTypeKind.S64:
                ExpectNumber(value, despecialized);
                result.Add(WasmValue.FromI64((long)value.Bits));
                break;
            case ComponentValueTypeKind.F32:
                Expect(value, ComponentValueKind.F32, type);
                result.Add(WasmValue.FromF32Bits((int)CanonicalizeF32((uint)value.Bits)));
                break;
            case ComponentValueTypeKind.F64:
                Expect(value, ComponentValueKind.F64, type);
                result.Add(WasmValue.FromF64Bits((long)CanonicalizeF64(value.Bits)));
                break;
            case ComponentValueTypeKind.Char:
                Expect(value, ComponentValueKind.Char, type);
                result.Add(WasmValue.FromI32((int)value.Bits));
                break;
            case ComponentValueTypeKind.String:
                Expect(value, ComponentValueKind.String, type);
                var (pointer, length) = StoreStringIntoRange(cx, value);
                AddPointer(cx, result, pointer);
                AddPointer(cx, result, length);
                break;
            case ComponentValueTypeKind.ErrorContext:
                Expect(value, ComponentValueKind.ErrorContext, type);
                result.Add(WasmValue.FromI32(cx.Instance.Handles.Add(new ErrorContextHandle(value.AsErrorContext()))));
                break;
            case ComponentValueTypeKind.List:
                Expect(value, ComponentValueKind.List, type);
                var list = (ComponentListType)despecialized;
                if (list.FixedLength is { } fixedLength)
                {
                    if (value.ListLength != fixedLength)
                        throw new WasmTypeMismatchException("The list " + value + " does not have " + fixedLength.ToString(System.Globalization.CultureInfo.InvariantCulture) + " elements");

                    for (var i = 0; i < fixedLength; i++)
                    {
                        LowerFlat(cx, value.GetListElement(i), list.ElementType, result);
                    }
                }
                else
                {
                    var (listPointer, listLength) = StoreListIntoRange(cx, value, list.ElementType);
                    AddPointer(cx, result, listPointer);
                    AddPointer(cx, result, listLength);
                }

                break;
            case ComponentValueTypeKind.Record:
                Expect(value, ComponentValueKind.Record, type);
                var record = (ComponentRecordType)despecialized;
                var fields = value.RecordFields;
                if (fields.Length != record.Fields.Length)
                    throw new WasmTypeMismatchException("The value " + value + " does not have the fields of " + type);

                for (var i = 0; i < fields.Length; i++)
                {
                    LowerFlat(cx, fields[i], record.Fields[i].Type, result);
                }

                break;
            case ComponentValueTypeKind.Variant:
                LowerFlatVariant(cx, value, (ComponentVariantType)despecialized, type, result);
                break;
            case ComponentValueTypeKind.Flags:
                result.Add(WasmValue.FromI32((int)PackFlags(value, (ComponentFlagsType)despecialized)));
                break;
            case ComponentValueTypeKind.Own:
                result.Add(WasmValue.FromI32(AddOwn(cx, value, (ComponentOwnType)despecialized)));
                break;
            case ComponentValueTypeKind.Borrow:
                var borrow = (ComponentBorrowType)despecialized;
                if (PassRepNotHandle(cx, borrow))
                {
                    var representation = GetResource(value, borrow).GuestRepresentation;
                    result.Add(borrow.Resource.Is64 ? WasmValue.FromI64(representation) : WasmValue.FromI32((int)representation));
                }
                else
                {
                    result.Add(WasmValue.FromI32(AddBorrow(cx, value, borrow)));
                }

                break;
            case ComponentValueTypeKind.Stream:
                result.Add(WasmValue.FromI32((int)ComponentStreams.LowerStream(cx, value, (ComponentStreamType)despecialized)));
                break;
            case ComponentValueTypeKind.Future:
                result.Add(WasmValue.FromI32((int)ComponentStreams.LowerFuture(cx, value, (ComponentFutureType)despecialized)));
                break;
            default:
                throw new InvalidOperationException("Unexpected type " + type);
        }
    }

    private static void AddPointer(LiftLowerContext cx, List<WasmValue> result, ulong value) => result.Add(cx.Options.Is64 ? WasmValue.FromI64((long)value) : WasmValue.FromI32((int)value));

    private static void LowerFlatVariant(LiftLowerContext cx, ComponentValue value, ComponentVariantType variant, ComponentValueType type, List<WasmValue> result)
    {
        var (caseIndex, caseValue) = MatchCase(value, variant, type);
        var flatTypes = FlattenVariant(cx, variant);
        flatTypes.RemoveAt(0);
        result.Add(WasmValue.FromI32(caseIndex));
        var written = 0;
        if (variant.Cases[caseIndex].Type is { } caseType)
        {
            var payload = new List<WasmValue>();
            LowerFlat(cx, caseValue!.Value, caseType, payload);
            var caseFlat = new List<CoreValKind>();
            FlattenType(cx, caseType, caseFlat);
            for (var i = 0; i < payload.Count; i++)
            {
                var have = caseFlat[i];
                var want = flatTypes[i];
                var item = payload[i];
                result.Add((have, want) switch
                {
                    (CoreValKind.F32, CoreValKind.I32) => WasmValue.FromI32(item.F32Bits),
                    (CoreValKind.I32, CoreValKind.I64) => WasmValue.FromI64((uint)item.I32),
                    (CoreValKind.F32, CoreValKind.I64) => WasmValue.FromI64((uint)item.F32Bits),
                    (CoreValKind.F64, CoreValKind.I64) => WasmValue.FromI64(item.F64Bits),
                    _ => item,
                });
            }

            written = payload.Count;
        }

        for (var i = written; i < flatTypes.Count; i++)
        {
            result.Add(flatTypes[i] switch
            {
                CoreValKind.I64 => WasmValue.FromI64(0),
                CoreValKind.F32 => WasmValue.FromF32(0),
                CoreValKind.F64 => WasmValue.FromF64(0),
                _ => WasmValue.FromI32(0),
            });
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Values
    // ------------------------------------------------------------------------------------------------------------

    public static ComponentValue[] LiftFlatValues(LiftLowerContext cx, int maxFlat, CoreValueIterator values, ComponentValueType[] types)
    {
        var flatTypes = FlattenTypes(cx, types);
        if (flatTypes.Count > maxFlat)
        {
            var pointer = values.NextPointer(cx);
            var tuple = new ComponentTupleType(types);
            ComponentTrap.ThrowIf(pointer != AlignTo(pointer, Alignment(cx, tuple)), "pointer not aligned");
            ComponentTrap.ThrowIf(pointer + ElemSize(cx, tuple) > cx.MemoryLength || pointer > cx.MemoryLength, "pointer out of bounds of memory");
            return Load(cx, pointer, tuple).RecordFields;
        }

        var result = new ComponentValue[types.Length];
        for (var i = 0; i < types.Length; i++)
        {
            result[i] = LiftFlat(cx, values, types[i]);
        }

        return result;
    }

    public static List<WasmValue> LowerFlatValues(LiftLowerContext cx, int maxFlat, ComponentValue[] values, ComponentValueType[] types, CoreValueIterator? outParameter = null)
    {
        if (values.Length != types.Length)
            throw new WasmTypeMismatchException("The number of values does not match the type: " + types.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " values are expected");

        var flatTypes = FlattenTypes(cx, types);
        var result = new List<WasmValue>();
        if (flatTypes.Count > maxFlat)
        {
            var tuple = new ComponentTupleType(types);
            ulong pointer;
            if (outParameter is null)
            {
                pointer = cx.Allocate(Alignment(cx, tuple), ElemSize(cx, tuple));
                AddPointer(cx, result, pointer);
            }
            else
            {
                pointer = outParameter.NextPointer(cx);
                ComponentTrap.ThrowIf(pointer != AlignTo(pointer, Alignment(cx, tuple)), "pointer not aligned");
                ComponentTrap.ThrowIf(pointer + ElemSize(cx, tuple) > cx.MemoryLength || pointer > cx.MemoryLength, "pointer out of bounds of memory");
            }

            Store(cx, ComponentValue.FromRecord(values), tuple, pointer);
            return result;
        }

        for (var i = 0; i < values.Length; i++)
        {
            LowerFlat(cx, values[i], types[i], result);
        }

        return result;
    }

    /// <summary>The kind of a core value type.</summary>
    public static CoreValKind GetKind(WasmValueType type) => type.Kind switch
    {
        WasmValueKind.I32 => CoreValKind.I32,
        WasmValueKind.I64 => CoreValKind.I64,
        WasmValueKind.F32 => CoreValKind.F32,
        WasmValueKind.F64 => CoreValKind.F64,
        WasmValueKind.V128 => CoreValKind.V128,
        _ => CoreValKind.Ref,
    };

    public static WasmValueType GetValueType(CoreValKind kind) => kind switch
    {
        CoreValKind.I32 => WasmValueType.I32,
        CoreValKind.I64 => WasmValueType.I64,
        CoreValKind.F32 => WasmValueType.F32,
        CoreValKind.F64 => WasmValueType.F64,
        CoreValKind.V128 => WasmValueType.V128,
        _ => WasmValueType.FuncRef,
    };
}
