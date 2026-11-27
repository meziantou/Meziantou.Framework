using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Meziantou.Framework.WasmEngine.Components;

#pragma warning disable CA1720 // The names are the names of the value types of the component model (char, string)

/// <summary>The kinds of the values of the component model (see <see cref="ComponentValue"/>).</summary>
public enum ComponentValueKind
{
    /// <summary>The default value: no value.</summary>
    None,

    /// <summary>A <c>bool</c>.</summary>
    Bool,

    /// <summary>An <c>s8</c>.</summary>
    S8,

    /// <summary>A <c>u8</c>.</summary>
    U8,

    /// <summary>An <c>s16</c>.</summary>
    S16,

    /// <summary>A <c>u16</c>.</summary>
    U16,

    /// <summary>An <c>s32</c>.</summary>
    S32,

    /// <summary>A <c>u32</c>.</summary>
    U32,

    /// <summary>An <c>s64</c>.</summary>
    S64,

    /// <summary>A <c>u64</c>.</summary>
    U64,

    /// <summary>An <c>f32</c>.</summary>
    F32,

    /// <summary>An <c>f64</c>.</summary>
    F64,

    /// <summary>A <c>char</c>.</summary>
    Char,

    /// <summary>A <c>string</c>.</summary>
    String,

    /// <summary>A <c>list</c> (or a <c>map</c>: a list of key-value records).</summary>
    List,

    /// <summary>A <c>record</c> or a <c>tuple</c>: the values of the fields, in the order of the type.</summary>
    Record,

    /// <summary>A case of a <c>variant</c>, an <c>enum</c>, an <c>option</c> (<c>none</c>, <c>some</c>) or a <c>result</c> (<c>ok</c>, <c>error</c>).</summary>
    Case,

    /// <summary>A <c>flags</c> value: the set labels.</summary>
    Flags,

    /// <summary>A handle to a resource (<c>own</c>, <c>borrow</c>).</summary>
    Resource,

    /// <summary>The readable end of a <c>stream</c>.</summary>
    Stream,

    /// <summary>The readable end of a <c>future</c>.</summary>
    Future,

    /// <summary>An <c>error-context</c>.</summary>
    ErrorContext,
}

/// <summary>
/// A value of the component model, converted from or to the memory of a component by the canonical ABI. The values are
/// immutable; their interpretation depends on their type: a record and a tuple are the values of their fields, a variant,
/// an enum, an option and a result are a case (a label and an optional payload).
/// </summary>
public readonly struct ComponentValue : IEquatable<ComponentValue>
{
    private readonly ulong _bits;
    private readonly object? _object;

    private ComponentValue(ComponentValueKind kind, ulong bits, object? value)
    {
        Kind = kind;
        _bits = bits;
        _object = value;
    }

    /// <summary>Gets the kind of the value.</summary>
    public ComponentValueKind Kind { get; }

    internal ulong Bits => _bits;

    internal object? Object => _object;

    /// <summary>Creates a <c>bool</c>.</summary>
    public static ComponentValue FromBool(bool value) => new(ComponentValueKind.Bool, value ? 1UL : 0UL, null);

    /// <summary>Creates an <c>s8</c>.</summary>
    public static ComponentValue FromS8(sbyte value) => new(ComponentValueKind.S8, (ulong)(long)value, null);

    /// <summary>Creates a <c>u8</c>.</summary>
    public static ComponentValue FromU8(byte value) => new(ComponentValueKind.U8, value, null);

    /// <summary>Creates an <c>s16</c>.</summary>
    public static ComponentValue FromS16(short value) => new(ComponentValueKind.S16, (ulong)(long)value, null);

    /// <summary>Creates a <c>u16</c>.</summary>
    public static ComponentValue FromU16(ushort value) => new(ComponentValueKind.U16, value, null);

    /// <summary>Creates an <c>s32</c>.</summary>
    public static ComponentValue FromS32(int value) => new(ComponentValueKind.S32, (ulong)(long)value, null);

    /// <summary>Creates a <c>u32</c>.</summary>
    public static ComponentValue FromU32(uint value) => new(ComponentValueKind.U32, value, null);

    /// <summary>Creates an <c>s64</c>.</summary>
    public static ComponentValue FromS64(long value) => new(ComponentValueKind.S64, (ulong)value, null);

    /// <summary>Creates a <c>u64</c>.</summary>
    public static ComponentValue FromU64(ulong value) => new(ComponentValueKind.U64, value, null);

    /// <summary>Creates an <c>f32</c>.</summary>
    public static ComponentValue FromF32(float value) => new(ComponentValueKind.F32, (uint)BitConverter.SingleToInt32Bits(value), null);

    /// <summary>Creates an <c>f64</c>.</summary>
    public static ComponentValue FromF64(double value) => new(ComponentValueKind.F64, (ulong)BitConverter.DoubleToInt64Bits(value), null);

    /// <summary>Creates a <c>char</c>.</summary>
    public static ComponentValue FromChar(Rune value) => new(ComponentValueKind.Char, (uint)value.Value, null);

    /// <summary>Creates a <c>string</c>. A string with an unpaired surrogate cannot be given to a component.</summary>
    public static ComponentValue FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(ComponentValueKind.String, 0, value);
    }

    internal static ComponentValue FromString(ComponentString value) => new(ComponentValueKind.String, 0, value);

    /// <summary>Creates a <c>list</c>.</summary>
    public static ComponentValue FromList(IEnumerable<ComponentValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new(ComponentValueKind.List, 0, values.ToArray());
    }

    internal static ComponentValue FromListArray(ComponentValue[] values) => new(ComponentValueKind.List, 0, values);

    /// <summary>Creates a <c>list</c> of <c>u8</c> from bytes, which are copied.</summary>
    public static ComponentValue FromBytes(ReadOnlySpan<byte> bytes) => new(ComponentValueKind.List, 0, bytes.ToArray());

    internal static ComponentValue FromByteArray(byte[] bytes) => new(ComponentValueKind.List, 0, bytes);

    /// <summary>Creates a <c>record</c> or a <c>tuple</c> from the values of its fields, in the order of the type.</summary>
    public static ComponentValue FromRecord(params ReadOnlySpan<ComponentValue> fields) => new(ComponentValueKind.Record, 0, fields.ToArray());

    /// <summary>Creates a <c>tuple</c> from the values of its fields.</summary>
    public static ComponentValue FromTuple(params ReadOnlySpan<ComponentValue> fields) => FromRecord(fields);

    /// <summary>Creates a case of a <c>variant</c>, an <c>enum</c>, an <c>option</c> or a <c>result</c>.</summary>
    /// <param name="name">The label of the case.</param>
    /// <param name="payload">The payload of the case, or null.</param>
    public static ComponentValue FromCase(string name, ComponentValue? payload = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        return new(ComponentValueKind.Case, 0, new ComponentCaseValue(name, payload));
    }

    /// <summary>Creates a case of an <c>enum</c>.</summary>
    public static ComponentValue FromEnum(string name) => FromCase(name);

    /// <summary>Gets the <c>none</c> case of an <c>option</c>.</summary>
    public static ComponentValue NoneValue { get; } = FromCase("none");

    /// <summary>Creates the <c>some</c> case of an <c>option</c>.</summary>
    public static ComponentValue Some(ComponentValue value) => FromCase("some", value);

    /// <summary>Creates an <c>option</c>: <c>some</c> with the value, or <c>none</c>.</summary>
    public static ComponentValue FromOption(ComponentValue? value) => value is { } some ? Some(some) : NoneValue;

    /// <summary>Creates the <c>ok</c> case of a <c>result</c>.</summary>
    public static ComponentValue Ok(ComponentValue? value = null) => FromCase("ok", value);

    /// <summary>Creates the <c>error</c> case of a <c>result</c>.</summary>
    public static ComponentValue Error(ComponentValue? value = null) => FromCase("error", value);

    /// <summary>Creates a <c>flags</c> value from the set labels.</summary>
    public static ComponentValue FromFlags(IEnumerable<string> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        return new(ComponentValueKind.Flags, 0, labels.ToArray());
    }

    /// <summary>Creates a handle to a resource, for an <c>own</c> or a <c>borrow</c> type.</summary>
    public static ComponentValue FromResource(ComponentResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return new(ComponentValueKind.Resource, 0, resource);
    }

    /// <summary>Creates an <c>error-context</c>.</summary>
    public static ComponentValue FromErrorContext(ComponentErrorContext errorContext)
    {
        ArgumentNullException.ThrowIfNull(errorContext);
        return new(ComponentValueKind.ErrorContext, 0, errorContext);
    }

    internal static ComponentValue FromStreamEnd(object end) => new(ComponentValueKind.Stream, 0, end);

    internal static ComponentValue FromFutureEnd(object end) => new(ComponentValueKind.Future, 0, end);

    private void Expect(ComponentValueKind kind)
    {
        if (Kind != kind)
            throw new InvalidOperationException("The value is a " + Kind.ToString() + ", not a " + kind.ToString());
    }

    /// <summary>Returns the value of a <c>bool</c>.</summary>
    public bool AsBool()
    {
        Expect(ComponentValueKind.Bool);
        return _bits != 0;
    }

    /// <summary>Returns the value of an <c>s8</c>.</summary>
    public sbyte AsS8()
    {
        Expect(ComponentValueKind.S8);
        return (sbyte)_bits;
    }

    /// <summary>Returns the value of a <c>u8</c>.</summary>
    public byte AsU8()
    {
        Expect(ComponentValueKind.U8);
        return (byte)_bits;
    }

    /// <summary>Returns the value of an <c>s16</c>.</summary>
    public short AsS16()
    {
        Expect(ComponentValueKind.S16);
        return (short)_bits;
    }

    /// <summary>Returns the value of a <c>u16</c>.</summary>
    public ushort AsU16()
    {
        Expect(ComponentValueKind.U16);
        return (ushort)_bits;
    }

    /// <summary>Returns the value of an <c>s32</c>.</summary>
    public int AsS32()
    {
        Expect(ComponentValueKind.S32);
        return (int)_bits;
    }

    /// <summary>Returns the value of a <c>u32</c>.</summary>
    public uint AsU32()
    {
        Expect(ComponentValueKind.U32);
        return (uint)_bits;
    }

    /// <summary>Returns the value of an <c>s64</c>.</summary>
    public long AsS64()
    {
        Expect(ComponentValueKind.S64);
        return (long)_bits;
    }

    /// <summary>Returns the value of a <c>u64</c>.</summary>
    public ulong AsU64()
    {
        Expect(ComponentValueKind.U64);
        return _bits;
    }

    /// <summary>Returns the value of an <c>f32</c>.</summary>
    public float AsF32()
    {
        Expect(ComponentValueKind.F32);
        return BitConverter.Int32BitsToSingle((int)_bits);
    }

    /// <summary>Returns the value of an <c>f64</c>.</summary>
    public double AsF64()
    {
        Expect(ComponentValueKind.F64);
        return BitConverter.Int64BitsToDouble((long)_bits);
    }

    /// <summary>Returns the value of a <c>char</c>.</summary>
    public Rune AsChar()
    {
        Expect(ComponentValueKind.Char);
        return new Rune((int)_bits);
    }

    /// <summary>Returns the value of a <c>string</c>.</summary>
    public string AsString()
    {
        Expect(ComponentValueKind.String);
        return _object is ComponentString value ? value.Value : (string)_object!;
    }

    /// <summary>Returns the elements of a <c>list</c>.</summary>
    public IReadOnlyList<ComponentValue> AsList()
    {
        Expect(ComponentValueKind.List);
        return _object is byte[] bytes ? Array.ConvertAll(bytes, FromU8) : (ComponentValue[])_object!;
    }

    /// <summary>Returns the bytes of a <c>list</c> of <c>u8</c>.</summary>
    public byte[] AsBytes()
    {
        Expect(ComponentValueKind.List);
        return _object switch
        {
            byte[] bytes => bytes,
            ComponentValue[] values => Array.ConvertAll(values, value => value.AsU8()),
            _ => [],
        };
    }

    /// <summary>The bytes of a list of <c>u8</c> created from bytes, without copy, or null.</summary>
    internal byte[]? ByteArray => _object as byte[];

    /// <summary>The number of elements of a list.</summary>
    internal int ListLength => _object switch
    {
        byte[] bytes => bytes.Length,
        ComponentValue[] values => values.Length,
        _ => 0,
    };

    internal ComponentValue GetListElement(int index) => _object is byte[] bytes ? FromU8(bytes[index]) : ((ComponentValue[])_object!)[index];

    /// <summary>Returns the values of the fields of a <c>record</c> or a <c>tuple</c>.</summary>
    public IReadOnlyList<ComponentValue> AsRecord()
    {
        Expect(ComponentValueKind.Record);
        return (ComponentValue[])_object!;
    }

    internal ComponentValue[] RecordFields => (ComponentValue[])_object!;

    /// <summary>Gets the label of a case of a <c>variant</c>, an <c>enum</c>, an <c>option</c> or a <c>result</c>.</summary>
    public string CaseName
    {
        get
        {
            Expect(ComponentValueKind.Case);
            return ((ComponentCaseValue)_object!).Name;
        }
    }

    /// <summary>Gets the payload of a case, or null.</summary>
    public ComponentValue? Payload
    {
        get
        {
            Expect(ComponentValueKind.Case);
            return ((ComponentCaseValue)_object!).Payload;
        }
    }

    /// <summary>Returns whether the value is the case <paramref name="name"/>.</summary>
    public bool IsCase(string name) => Kind == ComponentValueKind.Case && string.Equals(((ComponentCaseValue)_object!).Name, name, StringComparison.Ordinal);

    /// <summary>Returns the set labels of a <c>flags</c> value.</summary>
    public IReadOnlyList<string> AsFlags()
    {
        Expect(ComponentValueKind.Flags);
        return (string[])_object!;
    }

    /// <summary>Returns the resource of a handle.</summary>
    public ComponentResource AsResource()
    {
        Expect(ComponentValueKind.Resource);
        return (ComponentResource)_object!;
    }

    /// <summary>Returns the readable end of a <c>stream</c>, which the host reads.</summary>
    public ComponentStreamReader AsStreamReader()
    {
        Expect(ComponentValueKind.Stream);
        return new ComponentStreamReader((Runtime.ReadableStreamEnd)_object!);
    }

    /// <summary>Returns the readable end of a <c>future</c>, which the host reads.</summary>
    public ComponentFutureReader AsFutureReader()
    {
        Expect(ComponentValueKind.Future);
        return new ComponentFutureReader((Runtime.ReadableFutureEnd)_object!);
    }

    /// <summary>Returns the <c>error-context</c>.</summary>
    public ComponentErrorContext AsErrorContext()
    {
        Expect(ComponentValueKind.ErrorContext);
        return (ComponentErrorContext)_object!;
    }

    /// <inheritdoc/>
    public bool Equals(ComponentValue other)
    {
        if (Kind != other.Kind)
            return false;

        switch (Kind)
        {
            case ComponentValueKind.F32:
                return AsF32().Equals(other.AsF32());

            case ComponentValueKind.F64:
                return AsF64().Equals(other.AsF64());

            case ComponentValueKind.String:
                return string.Equals(AsString(), other.AsString(), StringComparison.Ordinal);

            case ComponentValueKind.List:
                if (ListLength != other.ListLength)
                    return false;

                for (var i = 0; i < ListLength; i++)
                {
                    if (!GetListElement(i).Equals(other.GetListElement(i)))
                        return false;
                }

                return true;

            case ComponentValueKind.Record:
                return RecordFields.AsSpan().SequenceEqual(other.RecordFields);

            case ComponentValueKind.Case:
                var left = (ComponentCaseValue)_object!;
                var right = (ComponentCaseValue)other._object!;
                return string.Equals(left.Name, right.Name, StringComparison.Ordinal) && Nullable.Equals(left.Payload, right.Payload);

            case ComponentValueKind.Flags:
                return AsFlags().Order(StringComparer.Ordinal).SequenceEqual(other.AsFlags().Order(StringComparer.Ordinal), StringComparer.Ordinal);

            case ComponentValueKind.Resource or ComponentValueKind.Stream or ComponentValueKind.Future or ComponentValueKind.ErrorContext:
                return ReferenceEquals(_object, other._object);

            default:
                return _bits == other._bits;
        }
    }

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ComponentValue other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Kind switch
    {
        ComponentValueKind.String => HashCode.Combine(Kind, StringComparer.Ordinal.GetHashCode(AsString())),
        ComponentValueKind.List or ComponentValueKind.Record or ComponentValueKind.Flags => HashCode.Combine(Kind, ListLength),
        ComponentValueKind.Case => HashCode.Combine(Kind, StringComparer.Ordinal.GetHashCode(CaseName)),
        _ => HashCode.Combine(Kind, _bits, _object),
    };

    /// <summary>Indicates whether two values are equal.</summary>
    public static bool operator ==(ComponentValue left, ComponentValue right) => left.Equals(right);

    /// <summary>Indicates whether two values are not equal.</summary>
    public static bool operator !=(ComponentValue left, ComponentValue right) => !left.Equals(right);

    /// <summary>Returns the value in a form similar to the text format.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        Write(builder);
        return builder.ToString();
    }

    private void Write(StringBuilder builder)
    {
        switch (Kind)
        {
            case ComponentValueKind.Bool:
                builder.Append(AsBool() ? "true" : "false");
                break;

            case ComponentValueKind.S8 or ComponentValueKind.S16 or ComponentValueKind.S32 or ComponentValueKind.S64:
                builder.Append(((long)_bits).ToString(CultureInfo.InvariantCulture));
                break;

            case ComponentValueKind.U8 or ComponentValueKind.U16 or ComponentValueKind.U32 or ComponentValueKind.U64:
                builder.Append(_bits.ToString(CultureInfo.InvariantCulture));
                break;

            case ComponentValueKind.F32:
                builder.Append(AsF32().ToString("R", CultureInfo.InvariantCulture));
                break;

            case ComponentValueKind.F64:
                builder.Append(AsF64().ToString("R", CultureInfo.InvariantCulture));
                break;

            case ComponentValueKind.Char:
                Span<char> rune = stackalloc char[2];
                builder.Append('\'').Append(rune[..AsChar().EncodeToUtf16(rune)]).Append('\'');
                break;

            case ComponentValueKind.String:
                builder.Append('"').Append(AsString()).Append('"');
                break;

            case ComponentValueKind.List:
                builder.Append('[');
                for (var i = 0; i < ListLength; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(", ");
                    }

                    GetListElement(i).Write(builder);
                }

                builder.Append(']');
                break;

            case ComponentValueKind.Record:
                builder.Append('{');
                for (var i = 0; i < RecordFields.Length; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(", ");
                    }

                    RecordFields[i].Write(builder);
                }

                builder.Append('}');
                break;

            case ComponentValueKind.Case:
                builder.Append(CaseName);
                if (Payload is { } payload)
                {
                    builder.Append('(');
                    payload.Write(builder);
                    builder.Append(')');
                }

                break;

            case ComponentValueKind.Flags:
                builder.Append('{').Append(string.Join(", ", AsFlags())).Append('}');
                break;

            default:
                builder.Append(Kind.ToString());
                break;
        }
    }
}

/// <summary>A case value: a label and an optional payload.</summary>
internal sealed record ComponentCaseValue(string Name, ComponentValue? Payload);

/// <summary>The string encodings of the canonical ABI.</summary>
internal enum ComponentStringEncoding
{
    Utf8,
    Utf16,
    Latin1Utf16,
}

/// <summary>
/// A string lifted from the memory of a component, with its source encoding and its number of code units (for
/// latin1+utf16, whether the string is UTF-16): the canonical ABI uses them to compute the sizes it allocates in the
/// destination.
/// </summary>
internal sealed record ComponentString(string Value, ComponentStringEncoding Encoding, ulong CodeUnits, bool IsUtf16);

/// <summary>
/// A resource: a value of a resource type, referenced by handles. The representation of a resource implemented by the
/// host is a .NET object; the representation of a resource implemented by a component is an integer of the component.
/// </summary>
public sealed class ComponentResource
{
    /// <summary>Creates a resource implemented by the host.</summary>
    /// <param name="type">The resource type, created by the host.</param>
    /// <param name="representation">The representation of the resource.</param>
    /// <exception cref="ArgumentException">The resource type is not a host resource type.</exception>
    public ComponentResource(ComponentResourceType type, object? representation)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!type.IsHostResource)
            throw new ArgumentException("The resource type is implemented by a component", nameof(type));

        Type = type;
        Representation = representation;
    }

    internal ComponentResource(ComponentResourceType type, long guestRepresentation)
    {
        Type = type;
        GuestRepresentation = guestRepresentation;
    }

    /// <summary>Gets the resource type.</summary>
    public ComponentResourceType Type { get; }

    /// <summary>Gets the representation of a resource implemented by the host, or null.</summary>
    public object? Representation { get; }

    /// <summary>The representation of a resource implemented by a component: its integer.</summary>
    internal long GuestRepresentation { get; }

    /// <summary>Whether the destructor of the resource ran.</summary>
    internal bool Dropped { get; set; }

    /// <inheritdoc/>
    public override string ToString() => Type.IsHostResource ? Type + "(" + Representation + ")" : Type + "(" + GuestRepresentation.ToString(CultureInfo.InvariantCulture) + ")";
}

/// <summary>An <c>error-context</c>: an error that components pass to each other, with a message for the diagnostics.</summary>
public sealed class ComponentErrorContext
{
    /// <summary>Initializes a new instance of the <see cref="ComponentErrorContext"/> class.</summary>
    /// <param name="debugMessage">The message for the diagnostics.</param>
    public ComponentErrorContext(string debugMessage)
    {
        ArgumentNullException.ThrowIfNull(debugMessage);
        DebugMessage = debugMessage;
    }

    /// <summary>Gets the message for the diagnostics.</summary>
    public string DebugMessage { get; }

    /// <inheritdoc/>
    public override string ToString() => DebugMessage;
}
