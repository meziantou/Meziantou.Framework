using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Meziantou.Framework.WasmEngine.Components;

#pragma warning disable CA1720 // The names are the names of the value types of the component model (char, string)

/// <summary>The kinds of the value types of the component model.</summary>
public enum ComponentValueTypeKind
{
    /// <summary><c>bool</c>.</summary>
    Bool,

    /// <summary><c>s8</c>.</summary>
    S8,

    /// <summary><c>u8</c>.</summary>
    U8,

    /// <summary><c>s16</c>.</summary>
    S16,

    /// <summary><c>u16</c>.</summary>
    U16,

    /// <summary><c>s32</c>.</summary>
    S32,

    /// <summary><c>u32</c>.</summary>
    U32,

    /// <summary><c>s64</c>.</summary>
    S64,

    /// <summary><c>u64</c>.</summary>
    U64,

    /// <summary><c>f32</c>.</summary>
    F32,

    /// <summary><c>f64</c>.</summary>
    F64,

    /// <summary><c>char</c>: a Unicode scalar value.</summary>
    Char,

    /// <summary><c>string</c>.</summary>
    String,

    /// <summary><c>error-context</c>.</summary>
    ErrorContext,

    /// <summary><c>list</c>.</summary>
    List,

    /// <summary><c>record</c>.</summary>
    Record,

    /// <summary><c>tuple</c>.</summary>
    Tuple,

    /// <summary><c>variant</c>.</summary>
    Variant,

    /// <summary><c>enum</c>.</summary>
    Enum,

    /// <summary><c>option</c>.</summary>
    Option,

    /// <summary><c>result</c>.</summary>
    Result,

    /// <summary><c>flags</c>.</summary>
    Flags,

    /// <summary><c>own</c>: an owned handle to a resource.</summary>
    Own,

    /// <summary><c>borrow</c>: a borrowed handle to a resource.</summary>
    Borrow,

    /// <summary><c>stream</c>.</summary>
    Stream,

    /// <summary><c>future</c>.</summary>
    Future,

    /// <summary><c>map</c>.</summary>
    Map,
}

/// <summary>A type of the component model: a value type, a function type, a resource type, a component type or an instance type.</summary>
public abstract class ComponentType
{
    private protected ComponentType()
    {
    }
}

/// <summary>
/// A value type of the component model. The value types are structural: two value types are equal when they have the
/// same structure, except the handles, whose resource types are compared by identity.
/// </summary>
public abstract class ComponentValueType : ComponentType, IEquatable<ComponentValueType>
{
    private protected ComponentValueType(ComponentValueTypeKind kind)
    {
        Kind = kind;
    }

    /// <summary>Gets the kind of the type.</summary>
    public ComponentValueTypeKind Kind { get; }

    /// <summary>The <c>bool</c> type.</summary>
    public static ComponentPrimitiveType Bool { get; } = new(ComponentValueTypeKind.Bool);

    /// <summary>The <c>s8</c> type.</summary>
    public static ComponentPrimitiveType S8 { get; } = new(ComponentValueTypeKind.S8);

    /// <summary>The <c>u8</c> type.</summary>
    public static ComponentPrimitiveType U8 { get; } = new(ComponentValueTypeKind.U8);

    /// <summary>The <c>s16</c> type.</summary>
    public static ComponentPrimitiveType S16 { get; } = new(ComponentValueTypeKind.S16);

    /// <summary>The <c>u16</c> type.</summary>
    public static ComponentPrimitiveType U16 { get; } = new(ComponentValueTypeKind.U16);

    /// <summary>The <c>s32</c> type.</summary>
    public static ComponentPrimitiveType S32 { get; } = new(ComponentValueTypeKind.S32);

    /// <summary>The <c>u32</c> type.</summary>
    public static ComponentPrimitiveType U32 { get; } = new(ComponentValueTypeKind.U32);

    /// <summary>The <c>s64</c> type.</summary>
    public static ComponentPrimitiveType S64 { get; } = new(ComponentValueTypeKind.S64);

    /// <summary>The <c>u64</c> type.</summary>
    public static ComponentPrimitiveType U64 { get; } = new(ComponentValueTypeKind.U64);

    /// <summary>The <c>f32</c> type.</summary>
    public static ComponentPrimitiveType F32 { get; } = new(ComponentValueTypeKind.F32);

    /// <summary>The <c>f64</c> type.</summary>
    public static ComponentPrimitiveType F64 { get; } = new(ComponentValueTypeKind.F64);

    /// <summary>The <c>char</c> type.</summary>
    public static ComponentPrimitiveType Char { get; } = new(ComponentValueTypeKind.Char);

    /// <summary>The <c>string</c> type.</summary>
    public static ComponentPrimitiveType String { get; } = new(ComponentValueTypeKind.String);

    /// <summary>The <c>error-context</c> type.</summary>
    public static ComponentPrimitiveType ErrorContext { get; } = new(ComponentValueTypeKind.ErrorContext);

    /// <inheritdoc/>
    public abstract bool Equals([NotNullWhen(true)] ComponentValueType? other);

    /// <inheritdoc/>
    public sealed override bool Equals([NotNullWhen(true)] object? obj) => obj is ComponentValueType other && Equals(other);

    /// <inheritdoc/>
    public abstract override int GetHashCode();

    /// <summary>Returns the type in the text format, such as <c>(list u8)</c>.</summary>
    public sealed override string ToString()
    {
        var builder = new StringBuilder();
        Write(builder);
        return builder.ToString();
    }

    internal abstract void Write(StringBuilder builder);

    internal static bool AreEqual(ComponentValueType? left, ComponentValueType? right) => left is null ? right is null : right is not null && left.Equals(right);

    internal static void Write(StringBuilder builder, ComponentValueType? type)
    {
        if (type is null)
        {
            builder.Append('_');
        }
        else
        {
            type.Write(builder);
        }
    }
}

/// <summary>A primitive value type: a number, <c>bool</c>, <c>char</c>, <c>string</c> or <c>error-context</c>.</summary>
public sealed class ComponentPrimitiveType : ComponentValueType
{
    internal ComponentPrimitiveType(ComponentValueTypeKind kind)
        : base(kind)
    {
    }

    /// <inheritdoc/>
    public override bool Equals(ComponentValueType? other) => other is ComponentPrimitiveType primitive && primitive.Kind == Kind;

    /// <inheritdoc/>
    public override int GetHashCode() => (int)Kind;

    internal override void Write(StringBuilder builder) => builder.Append(Kind switch
    {
        ComponentValueTypeKind.ErrorContext => "error-context",
        _ => Kind.ToString().ToLowerInvariant(),
    });
}

/// <summary>A <c>list</c> type: a sequence of values of an element type, of a fixed length or not.</summary>
public sealed class ComponentListType : ComponentValueType
{
    /// <summary>Initializes a new instance of the <see cref="ComponentListType"/> class.</summary>
    /// <param name="elementType">The type of the elements.</param>
    /// <param name="fixedLength">The length of a fixed-length list, or null.</param>
    public ComponentListType(ComponentValueType elementType, int? fixedLength = null)
        : base(ComponentValueTypeKind.List)
    {
        ArgumentNullException.ThrowIfNull(elementType);
        if (fixedLength is <= 0)
            throw new ArgumentOutOfRangeException(nameof(fixedLength), "The length of a fixed-length list must be positive");

        ElementType = elementType;
        FixedLength = fixedLength;
    }

    /// <summary>Gets the type of the elements.</summary>
    public ComponentValueType ElementType { get; }

    /// <summary>Gets the length of a fixed-length list, or null.</summary>
    public int? FixedLength { get; }

    /// <inheritdoc/>
    public override bool Equals(ComponentValueType? other) => other is ComponentListType list && list.FixedLength == FixedLength && list.ElementType.Equals(ElementType);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Kind, ElementType, FixedLength);

    internal override void Write(StringBuilder builder)
    {
        builder.Append("(list ");
        ElementType.Write(builder);
        if (FixedLength is { } length)
        {
            builder.Append(' ').Append(length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        builder.Append(')');
    }
}

/// <summary>A field of a <see cref="ComponentRecordType"/>, or a parameter of a <see cref="ComponentFunctionType"/>.</summary>
/// <param name="Name">The name of the field.</param>
/// <param name="Type">The type of the field.</param>
public readonly record struct ComponentField(string Name, ComponentValueType Type);

/// <summary>A case of a <see cref="ComponentVariantType"/>.</summary>
/// <param name="Name">The name of the case.</param>
/// <param name="Type">The type of the payload of the case, or null.</param>
public readonly record struct ComponentCase(string Name, ComponentValueType? Type);

/// <summary>A <c>record</c> type: named fields.</summary>
public sealed class ComponentRecordType : ComponentValueType
{
    /// <summary>Initializes a new instance of the <see cref="ComponentRecordType"/> class.</summary>
    /// <param name="fields">The fields, at least one.</param>
    public ComponentRecordType(IEnumerable<ComponentField> fields)
        : base(ComponentValueTypeKind.Record)
    {
        ArgumentNullException.ThrowIfNull(fields);
        Fields = [.. fields];
        if (Fields.Length == 0)
            throw new ArgumentException("A record has at least one field", nameof(fields));
    }

    /// <summary>Gets the fields.</summary>
    public ImmutableArray<ComponentField> Fields { get; }

    /// <inheritdoc/>
    public override bool Equals(ComponentValueType? other)
    {
        if (other is not ComponentRecordType record || record.Fields.Length != Fields.Length)
            return false;

        for (var i = 0; i < Fields.Length; i++)
        {
            if (!string.Equals(Fields[i].Name, record.Fields[i].Name, StringComparison.Ordinal) || !Fields[i].Type.Equals(record.Fields[i].Type))
                return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        foreach (var field in Fields)
        {
            hash.Add(field.Name, StringComparer.Ordinal);
            hash.Add(field.Type);
        }

        return hash.ToHashCode();
    }

    internal override void Write(StringBuilder builder)
    {
        builder.Append("(record");
        foreach (var field in Fields)
        {
            builder.Append(" (field \"").Append(field.Name).Append("\" ");
            field.Type.Write(builder);
            builder.Append(')');
        }

        builder.Append(')');
    }
}

/// <summary>A <c>tuple</c> type: unnamed fields.</summary>
public sealed class ComponentTupleType : ComponentValueType
{
    /// <summary>Initializes a new instance of the <see cref="ComponentTupleType"/> class.</summary>
    /// <param name="types">The types of the fields, at least one.</param>
    public ComponentTupleType(IEnumerable<ComponentValueType> types)
        : base(ComponentValueTypeKind.Tuple)
    {
        ArgumentNullException.ThrowIfNull(types);
        Types = [.. types];
        if (Types.Length == 0)
            throw new ArgumentException("A tuple has at least one field", nameof(types));
    }

    /// <summary>Gets the types of the fields.</summary>
    public ImmutableArray<ComponentValueType> Types { get; }

    /// <inheritdoc/>
    public override bool Equals(ComponentValueType? other) => other is ComponentTupleType tuple && tuple.Types.AsSpan().SequenceEqual(Types.AsSpan());

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        foreach (var type in Types)
        {
            hash.Add(type);
        }

        return hash.ToHashCode();
    }

    internal override void Write(StringBuilder builder)
    {
        builder.Append("(tuple");
        foreach (var type in Types)
        {
            builder.Append(' ');
            type.Write(builder);
        }

        builder.Append(')');
    }
}

/// <summary>A <c>variant</c> type: one of several named cases, each with an optional payload.</summary>
public sealed class ComponentVariantType : ComponentValueType
{
    /// <summary>Initializes a new instance of the <see cref="ComponentVariantType"/> class.</summary>
    /// <param name="cases">The cases, at least one.</param>
    public ComponentVariantType(IEnumerable<ComponentCase> cases)
        : base(ComponentValueTypeKind.Variant)
    {
        ArgumentNullException.ThrowIfNull(cases);
        Cases = [.. cases];
        if (Cases.Length == 0)
            throw new ArgumentException("A variant has at least one case", nameof(cases));
    }

    /// <summary>Gets the cases.</summary>
    public ImmutableArray<ComponentCase> Cases { get; }

    /// <inheritdoc/>
    public override bool Equals(ComponentValueType? other)
    {
        if (other is not ComponentVariantType variant || variant.Cases.Length != Cases.Length)
            return false;

        for (var i = 0; i < Cases.Length; i++)
        {
            if (!string.Equals(Cases[i].Name, variant.Cases[i].Name, StringComparison.Ordinal) || !AreEqual(Cases[i].Type, variant.Cases[i].Type))
                return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        foreach (var @case in Cases)
        {
            hash.Add(@case.Name, StringComparer.Ordinal);
            hash.Add(@case.Type);
        }

        return hash.ToHashCode();
    }

    internal override void Write(StringBuilder builder)
    {
        builder.Append("(variant");
        foreach (var @case in Cases)
        {
            builder.Append(" (case \"").Append(@case.Name).Append('"');
            if (@case.Type is { } type)
            {
                builder.Append(' ');
                type.Write(builder);
            }

            builder.Append(')');
        }

        builder.Append(')');
    }
}

/// <summary>A type whose values are labels: an <c>enum</c> (one label) or <c>flags</c> (a set of labels).</summary>
public abstract class ComponentLabelsType : ComponentValueType
{
    private protected ComponentLabelsType(ComponentValueTypeKind kind, IEnumerable<string> labels)
        : base(kind)
    {
        ArgumentNullException.ThrowIfNull(labels);
        Labels = [.. labels];
        if (Labels.Length == 0)
            throw new ArgumentException("The type has at least one label", nameof(labels));
    }

    /// <summary>Gets the labels.</summary>
    public ImmutableArray<string> Labels { get; }

    /// <inheritdoc/>
    public override bool Equals(ComponentValueType? other) => other is ComponentLabelsType labels && labels.Kind == Kind && labels.Labels.AsSpan().SequenceEqual(Labels.AsSpan(), StringComparer.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        foreach (var label in Labels)
        {
            hash.Add(label, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    internal override void Write(StringBuilder builder)
    {
        builder.Append(Kind == ComponentValueTypeKind.Enum ? "(enum" : "(flags");
        foreach (var label in Labels)
        {
            builder.Append(" \"").Append(label).Append('"');
        }

        builder.Append(')');
    }
}

/// <summary>An <c>enum</c> type: one of several labels.</summary>
public sealed class ComponentEnumType : ComponentLabelsType
{
    /// <summary>Initializes a new instance of the <see cref="ComponentEnumType"/> class.</summary>
    /// <param name="labels">The labels, at least one.</param>
    public ComponentEnumType(IEnumerable<string> labels)
        : base(ComponentValueTypeKind.Enum, labels)
    {
    }
}

/// <summary>A <c>flags</c> type: a set of labels (1 to 32 labels).</summary>
public sealed class ComponentFlagsType : ComponentLabelsType
{
    /// <summary>Initializes a new instance of the <see cref="ComponentFlagsType"/> class.</summary>
    /// <param name="labels">The labels, 1 to 32.</param>
    public ComponentFlagsType(IEnumerable<string> labels)
        : base(ComponentValueTypeKind.Flags, labels)
    {
        if (Labels.Length > 32)
            throw new ArgumentException("A flags type has at most 32 labels", nameof(labels));
    }
}

/// <summary>An <c>option</c> type: a value of a type, or none.</summary>
public sealed class ComponentOptionType : ComponentValueType
{
    /// <summary>Initializes a new instance of the <see cref="ComponentOptionType"/> class.</summary>
    /// <param name="type">The type of the value.</param>
    public ComponentOptionType(ComponentValueType type)
        : base(ComponentValueTypeKind.Option)
    {
        ArgumentNullException.ThrowIfNull(type);
        Type = type;
    }

    /// <summary>Gets the type of the value.</summary>
    public ComponentValueType Type { get; }

    /// <inheritdoc/>
    public override bool Equals(ComponentValueType? other) => other is ComponentOptionType option && option.Type.Equals(Type);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Kind, Type);

    internal override void Write(StringBuilder builder)
    {
        builder.Append("(option ");
        Type.Write(builder);
        builder.Append(')');
    }
}

/// <summary>A <c>result</c> type: a success with an optional value, or an error with an optional value.</summary>
public sealed class ComponentResultType : ComponentValueType
{
    /// <summary>Initializes a new instance of the <see cref="ComponentResultType"/> class.</summary>
    /// <param name="ok">The type of the value of a success, or null.</param>
    /// <param name="error">The type of the value of an error, or null.</param>
    public ComponentResultType(ComponentValueType? ok, ComponentValueType? error)
        : base(ComponentValueTypeKind.Result)
    {
        Ok = ok;
        Error = error;
    }

    /// <summary>Gets the type of the value of a success, or null.</summary>
    public ComponentValueType? Ok { get; }

    /// <summary>Gets the type of the value of an error, or null.</summary>
    public ComponentValueType? Error { get; }

    /// <inheritdoc/>
    public override bool Equals(ComponentValueType? other) => other is ComponentResultType result && AreEqual(result.Ok, Ok) && AreEqual(result.Error, Error);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Kind, Ok, Error);

    internal override void Write(StringBuilder builder)
    {
        builder.Append("(result");
        if (Ok is { } ok)
        {
            builder.Append(' ');
            ok.Write(builder);
        }

        if (Error is { } error)
        {
            builder.Append(" (error ");
            error.Write(builder);
            builder.Append(')');
        }

        builder.Append(')');
    }
}

/// <summary>A handle type: <c>own</c> or <c>borrow</c> of a resource type.</summary>
public abstract class ComponentHandleType : ComponentValueType
{
    private protected ComponentHandleType(ComponentValueTypeKind kind, ComponentResourceType resource)
        : base(kind)
    {
        ArgumentNullException.ThrowIfNull(resource);
        Resource = resource;
    }

    /// <summary>Gets the resource type of the handle.</summary>
    public ComponentResourceType Resource { get; }

    /// <inheritdoc/>
    public override bool Equals(ComponentValueType? other) => other is ComponentHandleType handle && handle.Kind == Kind && ReferenceEquals(handle.Resource, Resource);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Kind, System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Resource));

    internal override void Write(StringBuilder builder) => builder.Append(Kind == ComponentValueTypeKind.Own ? "(own " : "(borrow ").Append(Resource.Name ?? "resource").Append(')');
}

/// <summary>An <c>own</c> handle type: the receiver owns the resource and must drop it.</summary>
public sealed class ComponentOwnType : ComponentHandleType
{
    /// <summary>Initializes a new instance of the <see cref="ComponentOwnType"/> class.</summary>
    /// <param name="resource">The resource type.</param>
    public ComponentOwnType(ComponentResourceType resource)
        : base(ComponentValueTypeKind.Own, resource)
    {
    }
}

/// <summary>A <c>borrow</c> handle type: the receiver can use the resource during the call.</summary>
public sealed class ComponentBorrowType : ComponentHandleType
{
    /// <summary>Initializes a new instance of the <see cref="ComponentBorrowType"/> class.</summary>
    /// <param name="resource">The resource type.</param>
    public ComponentBorrowType(ComponentResourceType resource)
        : base(ComponentValueTypeKind.Borrow, resource)
    {
    }
}

/// <summary>An asynchronous value type: a <c>stream</c> or a <c>future</c> of an optional element type.</summary>
public abstract class ComponentAsyncValueType : ComponentValueType
{
    private protected ComponentAsyncValueType(ComponentValueTypeKind kind, ComponentValueType? elementType)
        : base(kind)
    {
        ElementType = elementType;
    }

    /// <summary>Gets the type of the elements, or null.</summary>
    public ComponentValueType? ElementType { get; }

    /// <inheritdoc/>
    public override bool Equals(ComponentValueType? other) => other is ComponentAsyncValueType value && value.Kind == Kind && AreEqual(value.ElementType, ElementType);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Kind, ElementType);

    internal override void Write(StringBuilder builder)
    {
        builder.Append(Kind == ComponentValueTypeKind.Stream ? "(stream" : "(future");
        if (ElementType is { } element)
        {
            builder.Append(' ');
            element.Write(builder);
        }

        builder.Append(')');
    }
}

/// <summary>A <c>stream</c> type: values of an element type that a writer copies to a reader over time.</summary>
public sealed class ComponentStreamType : ComponentAsyncValueType
{
    /// <summary>Initializes a new instance of the <see cref="ComponentStreamType"/> class.</summary>
    /// <param name="elementType">The type of the elements, or null.</param>
    public ComponentStreamType(ComponentValueType? elementType)
        : base(ComponentValueTypeKind.Stream, elementType)
    {
    }
}

/// <summary>A <c>future</c> type: a value of an element type that a writer gives to a reader once.</summary>
public sealed class ComponentFutureType : ComponentAsyncValueType
{
    /// <summary>Initializes a new instance of the <see cref="ComponentFutureType"/> class.</summary>
    /// <param name="elementType">The type of the value, or null.</param>
    public ComponentFutureType(ComponentValueType? elementType)
        : base(ComponentValueTypeKind.Future, elementType)
    {
    }
}

/// <summary>A <c>map</c> type: entries of a key and a value.</summary>
public sealed class ComponentMapType : ComponentValueType
{
    /// <summary>Initializes a new instance of the <see cref="ComponentMapType"/> class.</summary>
    /// <param name="keyType">The type of the keys.</param>
    /// <param name="valueType">The type of the values.</param>
    public ComponentMapType(ComponentValueType keyType, ComponentValueType valueType)
        : base(ComponentValueTypeKind.Map)
    {
        ArgumentNullException.ThrowIfNull(keyType);
        ArgumentNullException.ThrowIfNull(valueType);
        KeyType = keyType;
        ValueType = valueType;
    }

    /// <summary>Gets the type of the keys.</summary>
    public ComponentValueType KeyType { get; }

    /// <summary>Gets the type of the values.</summary>
    public ComponentValueType ValueType { get; }

    /// <inheritdoc/>
    public override bool Equals(ComponentValueType? other) => other is ComponentMapType map && map.KeyType.Equals(KeyType) && map.ValueType.Equals(ValueType);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Kind, KeyType, ValueType);

    internal override void Write(StringBuilder builder)
    {
        builder.Append("(map ");
        KeyType.Write(builder);
        builder.Append(' ');
        ValueType.Write(builder);
        builder.Append(')');
    }
}

/// <summary>
/// A resource type: an abstract type whose values are handles (<see cref="ComponentOwnType"/>, <see cref="ComponentBorrowType"/>).
/// Resource types are compared by identity: each resource type definition of a component instance, and each resource type
/// created by the host, is a distinct type.
/// </summary>
public sealed class ComponentResourceType : ComponentType
{
    /// <summary>Creates a resource type implemented by the host: the representation of its resources is a .NET object.</summary>
    /// <param name="name">The name of the type, for the diagnostics.</param>
    /// <param name="destructor">The action called when the last owned handle of a resource is dropped, or null.</param>
    public ComponentResourceType(string? name = null, Action<ComponentResource>? destructor = null)
    {
        Name = name;
        HostDestructor = destructor;
    }

    internal ComponentResourceType(string? name, Runtime.ComponentInstanceState? implementation, bool is64, WasmFunction? destructor)
    {
        Name = name;
        Implementation = implementation;
        Is64 = is64;
        Destructor = destructor;
    }

    /// <summary>Gets the name of the type, for the diagnostics.</summary>
    public string? Name { get; }

    /// <summary>Gets whether the resources of the type are implemented by the host.</summary>
    public bool IsHostResource => Implementation is null && !IsAbstract;

    /// <summary>The component instance that defines the type, or null for a host resource type.</summary>
    internal Runtime.ComponentInstanceState? Implementation { get; }

    /// <summary>Whether the representation of the resources is an <c>i64</c> (else an <c>i32</c>).</summary>
    internal bool Is64 { get; }

    /// <summary>The destructor of a resource type defined by a component, or null.</summary>
    internal WasmFunction? Destructor { get; }

    /// <summary>The destructor of a resource type created by the host, or null.</summary>
    internal Action<ComponentResource>? HostDestructor { get; }

    /// <summary>Whether the type is an abstract type created by the validation of an import or an export (<c>sub resource</c>).</summary>
    internal bool IsAbstract { get; init; }

    /// <summary>
    /// The resource type that the type is an alias of, or the type itself. The validation gives a new identity to each
    /// type that is imported or exported (the identity names the type), but the aliases of a resource type are equal.
    /// </summary>
    internal ComponentResourceType Root => AliasOf ?? this;

    private ComponentResourceType? AliasOf { get; init; }

    /// <summary>Creates an alias of the type: a type with a new identity, equal to the type.</summary>
    internal ComponentResourceType CreateAlias() => new(Name, Implementation, Is64, Destructor) { IsAbstract = IsAbstract, AliasOf = Root };

    /// <inheritdoc/>
    public override string ToString() => Name ?? "resource";
}

/// <summary>A function type of the component model: named parameters, an optional result, and whether it is asynchronous.</summary>
public sealed class ComponentFunctionType : ComponentType, IEquatable<ComponentFunctionType>
{
    /// <summary>Initializes a new instance of the <see cref="ComponentFunctionType"/> class.</summary>
    /// <param name="parameters">The parameters.</param>
    /// <param name="result">The type of the result, or null.</param>
    /// <param name="isAsync">Whether the function is asynchronous (<c>async</c>).</param>
    public ComponentFunctionType(IEnumerable<ComponentField> parameters, ComponentValueType? result, bool isAsync = false)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        Parameters = [.. parameters];
        Result = result;
        IsAsync = isAsync;
    }

    /// <summary>Gets the parameters.</summary>
    public ImmutableArray<ComponentField> Parameters { get; }

    /// <summary>Gets the type of the result, or null.</summary>
    public ComponentValueType? Result { get; }

    /// <summary>Gets whether the function is asynchronous (<c>async</c>).</summary>
    public bool IsAsync { get; }

    /// <summary>The types of the parameters.</summary>
    internal ComponentValueType[] ParameterTypes => [.. Parameters.Select(parameter => parameter.Type)];

    /// <summary>The types of the results: none or one.</summary>
    internal ComponentValueType[] ResultTypes => Result is null ? [] : [Result];

    /// <inheritdoc/>
    public bool Equals([NotNullWhen(true)] ComponentFunctionType? other)
    {
        if (other is null || other.IsAsync != IsAsync || other.Parameters.Length != Parameters.Length || !ComponentValueType.AreEqual(other.Result, Result))
            return false;

        for (var i = 0; i < Parameters.Length; i++)
        {
            if (!string.Equals(Parameters[i].Name, other.Parameters[i].Name, StringComparison.Ordinal) || !Parameters[i].Type.Equals(other.Parameters[i].Type))
                return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ComponentFunctionType other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(IsAsync);
        hash.Add(Result);
        foreach (var parameter in Parameters)
        {
            hash.Add(parameter.Name, StringComparer.Ordinal);
            hash.Add(parameter.Type);
        }

        return hash.ToHashCode();
    }

    /// <summary>Returns the type in the text format.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder(IsAsync ? "(func async" : "(func");
        foreach (var parameter in Parameters)
        {
            builder.Append(" (param \"").Append(parameter.Name).Append("\" ");
            parameter.Type.Write(builder);
            builder.Append(')');
        }

        if (Result is { } result)
        {
            builder.Append(" (result ");
            result.Write(builder);
            builder.Append(')');
        }

        return builder.Append(')').ToString();
    }
}

/// <summary>The kinds of the definitions that a component imports or exports.</summary>
public enum ComponentExternKind
{
    /// <summary>A core module.</summary>
    CoreModule,

    /// <summary>A function.</summary>
    Function,

    /// <summary>A value.</summary>
    Value,

    /// <summary>A type.</summary>
    Type,

    /// <summary>A component.</summary>
    Component,

    /// <summary>An instance.</summary>
    Instance,
}

/// <summary>The type of an import or an export of a component, of an instance or of a component type.</summary>
public sealed class ComponentExternType
{
    internal ComponentExternType(ComponentExternKind kind, ComponentType? type, ComponentValueType? valueType = null, ComponentModuleType? moduleType = null)
    {
        Kind = kind;
        Type = type;
        ValueType = valueType;
        ModuleType = moduleType;
    }

    /// <summary>Gets the kind of the definition.</summary>
    public ComponentExternKind Kind { get; }

    /// <summary>
    /// Gets the type: a <see cref="ComponentFunctionType"/>, a <see cref="ComponentInstanceType"/>, a
    /// <see cref="ComponentComponentType"/>, or for a type, the type it is equal to (a <see cref="ComponentResourceType"/>
    /// for the abstract resource types); null for a core module or a value.
    /// </summary>
    public ComponentType? Type { get; }

    /// <summary>Gets the type of a value, or null.</summary>
    public ComponentValueType? ValueType { get; }

    /// <summary>Gets the type of a core module, or null.</summary>
    public ComponentModuleType? ModuleType { get; }

    /// <inheritdoc/>
    public override string ToString() => Kind switch
    {
        ComponentExternKind.CoreModule => "(core module)",
        ComponentExternKind.Value => "(value " + ValueType + ")",
        ComponentExternKind.Type => "(type " + Type + ")",
        _ => Type?.ToString() ?? Kind.ToString(),
    };
}

/// <summary>A named import or export of a component, of an instance or of a component type.</summary>
/// <param name="Name">The name.</param>
/// <param name="Type">The type.</param>
public readonly record struct ComponentExternDescriptor(string Name, ComponentExternType Type);

/// <summary>The type of a component instance: its exports.</summary>
public sealed class ComponentInstanceType : ComponentType
{
    internal ComponentInstanceType(IEnumerable<ComponentExternDescriptor> exports)
    {
        Exports = [.. exports];
    }

    /// <summary>Gets the exports.</summary>
    public ImmutableArray<ComponentExternDescriptor> Exports { get; }

    /// <summary>The resource types that the type defines (abstract exports): each import of the type defines new resource types.</summary>
    internal ImmutableArray<ComponentResourceType> DefinedResources { get; init; } = [];

    /// <summary>Returns the export whose name is <paramref name="name"/>, or null.</summary>
    /// <param name="name">The name of the export.</param>
    public ComponentExternType? GetExport(string name) => ComponentNames.Find(Exports, name);

    /// <inheritdoc/>
    public override string ToString() => "(instance" + string.Concat(Exports.Select(export => " (export \"" + export.Name + "\" " + export.Type + ")")) + ")";
}

/// <summary>The type of a component: its imports and its exports.</summary>
public sealed class ComponentComponentType : ComponentType
{
    internal ComponentComponentType(IEnumerable<ComponentExternDescriptor> imports, IEnumerable<ComponentExternDescriptor> exports)
    {
        Imports = [.. imports];
        Exports = [.. exports];
    }

    /// <summary>Gets the imports.</summary>
    public ImmutableArray<ComponentExternDescriptor> Imports { get; }

    /// <summary>Gets the exports.</summary>
    public ImmutableArray<ComponentExternDescriptor> Exports { get; }

    /// <summary>The resource types of the imports: the instantiation replaces them with the resource types of the arguments.</summary>
    internal ImmutableArray<ComponentResourceType> ImportedResources { get; init; } = [];

    /// <summary>The resource types that the component defines: each instantiation creates new resource types.</summary>
    internal ImmutableArray<ComponentResourceType> DefinedResources { get; init; } = [];

    /// <inheritdoc/>
    public override string ToString() => "(component" + string.Concat(Imports.Select(import => " (import \"" + import.Name + "\" " + import.Type + ")")) + string.Concat(Exports.Select(export => " (export \"" + export.Name + "\" " + export.Type + ")")) + ")";
}

/// <summary>The type of a core module: its imports and its exports.</summary>
public sealed class ComponentModuleType
{
    internal ComponentModuleType(IEnumerable<WasmImport> imports, IEnumerable<WasmExport> exports)
    {
        Imports = [.. imports];
        Exports = [.. exports];
    }

    /// <summary>Gets the imports.</summary>
    public ImmutableArray<WasmImport> Imports { get; }

    /// <summary>Gets the exports.</summary>
    public ImmutableArray<WasmExport> Exports { get; }
}

/// <summary>The names of the imports and the exports: the lookups ignore the version suffix when the exact name is missing.</summary>
internal static class ComponentNames
{
    public static ComponentExternType? Find(ImmutableArray<ComponentExternDescriptor> descriptors, string name)
    {
        foreach (var descriptor in descriptors)
        {
            if (string.Equals(descriptor.Name, name, StringComparison.Ordinal))
                return descriptor.Type;
        }

        return null;
    }
}
