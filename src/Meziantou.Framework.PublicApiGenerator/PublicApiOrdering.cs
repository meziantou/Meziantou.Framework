using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

// The order of the members, the attributes and the constraints of a declaration.
// It only depends on the declarations themselves, so the output does not change when a declaration is moved in the source code.
// The model read from metadata and the text built from reflection must use the same order.
internal static class PublicApiOrdering
{
    private const string OperatorPrefix = "op_";
    private const string CheckedOperatorPrefix = "op_Checked";

    // Attributes are compared as written in a C# declaration, without the brackets (e.g. System.Obsolete("message")): by name, then by arguments.
    // The brackets must not be compared: the closing bracket is after the letters, so "[A]" would be after "[AB]".
    public static StringComparer AttributeComparer => StringComparer.Ordinal;

    public static MemberSortKey GetMemberSortKey(PublicApiMember member)
    {
        return member switch
        {
            PublicApiField field => new MemberSortKey(MemberSortKind.Field, field.Name),
            PublicApiProperty property => new MemberSortKey(MemberSortKind.Property, property.Name, parameterCount: property.Parameters.Length, signature: GetSignatureSortText(property.Parameters)),
            PublicApiEvent @event => new MemberSortKey(MemberSortKind.Event, @event.Name),
            PublicApiMethod method => new MemberSortKey(
                GetMethodSortKind(method.MethodKind),
                method.MethodKind == PublicApiMethodKind.Constructor ? string.Empty : method.Name,
                method.GenericParameters.Length,
                method.Parameters.Length,
                GetSignatureSortText(method.Parameters),
                method.MethodKind == PublicApiMethodKind.Constructor ? string.Empty : GetTypeSortText(method.ReturnType)),
            _ => throw new ArgumentOutOfRangeException(nameof(member)),
        };
    }

    public static MemberSortKind GetOperatorSortKind(string metadataName)
    {
        return metadataName is "op_Implicit" or "op_Explicit" or "op_CheckedExplicit" ? MemberSortKind.Conversion : MemberSortKind.Operator;
    }

    private static MemberSortKind GetMethodSortKind(PublicApiMethodKind methodKind)
    {
        return methodKind switch
        {
            PublicApiMethodKind.Constructor => MemberSortKind.Constructor,
            PublicApiMethodKind.Destructor => MemberSortKind.Destructor,
            PublicApiMethodKind.Operator => MemberSortKind.Operator,
            PublicApiMethodKind.Conversion => MemberSortKind.Conversion,
            _ => MemberSortKind.Method,
        };
    }

    /// <summary>Gets the text of a type as written in a C# declaration, including its nullable annotations.</summary>
    public static string GetTypeSortText(PublicApiTypeReference type)
    {
        var writer = new DeclarationWriter("\n");
        CSharpCompilableFormatter.WriteTypeReference(writer, type, includeNullableAnnotations: true);
        return writer.GetText();
    }

    public static string GetSignatureSortText(ImmutableArray<PublicApiParameter> parameters)
    {
        if (parameters.IsEmpty)
            return string.Empty;

        return string.Join(", ", parameters.Select(static parameter => GetRefKindSortText(parameter.RefKind) + GetTypeSortText(parameter.Type)));
    }

    public static string GetRefKindSortText(PublicApiRefKind refKind)
    {
        return refKind switch
        {
            PublicApiRefKind.Ref => "ref ",
            PublicApiRefKind.Out => "out ",
            PublicApiRefKind.In => "in ",
            PublicApiRefKind.RefReadOnly => "ref readonly ",
            _ => string.Empty,
        };
    }

    // A value passed to a ref or an out parameter must be a variable, so "default(T)" cannot be used
    public static bool RequiresVariable(string refKindSortText) => refKindSortText is "ref " or "out ";

    /// <summary>Compares the constructors of a base type, to select the one called by the constructors of the derived types.</summary>
    public static int CompareBaseConstructors((bool RequiresVariable, int ParameterCount, string Signature) x, (bool RequiresVariable, int ParameterCount, string Signature) y)
    {
        var result = x.RequiresVariable.CompareTo(y.RequiresVariable);
        if (result != 0)
            return result;

        result = x.ParameterCount.CompareTo(y.ParameterCount);
        if (result != 0)
            return result;

        return string.CompareOrdinal(x.Signature, y.Signature);
    }

    /// <summary>Compares enum members by value, then by name.</summary>
    public static int CompareEnumMembers(object? xValue, string xName, object? yValue, string yName)
    {
        // A member without a value is before the other ones
        var result = Nullable.Compare(GetEnumSortValue(xValue), GetEnumSortValue(yValue));
        return result != 0 ? result : string.CompareOrdinal(xName, yName);
    }

    public static int CompareEnumMembers(PublicApiMember x, PublicApiMember y)
    {
        return CompareEnumMembers((x as PublicApiField)?.ConstantValue, x.Name, (y as PublicApiField)?.ConstantValue, y.Name);
    }

    // The values are not compared using their bits, as negative values must be before the positive ones
    private static Int128? GetEnumSortValue(object? value)
    {
        return value switch
        {
            sbyte v => v,
            byte v => v,
            short v => v,
            ushort v => v,
            int v => v,
            uint v => v,
            long v => v,
            ulong v => v,
            nint v => v,
            nuint v => v,
            char v => v,
            bool v => v ? 1 : 0,
            _ => null,
        };
    }

    public static (string ComparedName, string Name, string Qualifier) SplitMemberName(string metadataName)
    {
        // The name of an explicit interface implementation is prefixed by the name of the interface (e.g. "System.IDisposable.Dispose").
        // The type arguments of the interface can contain dots, but the name of the member cannot.
        var name = metadataName;
        var qualifier = string.Empty;
        var separatorIndex = metadataName.LastIndexOf('.', StringComparison.Ordinal);
        if (separatorIndex > 0)
        {
            name = metadataName[(separatorIndex + 1)..];
            qualifier = metadataName[..separatorIndex];
        }

        // A checked operator is next to the operator it is the checked version of
        var comparedName = name.StartsWith(CheckedOperatorPrefix, StringComparison.Ordinal) ? string.Concat(OperatorPrefix, name.AsSpan(CheckedOperatorPrefix.Length)) : name;
        return (comparedName, name, qualifier);
    }
}
