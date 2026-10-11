using System.Reflection.Metadata;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>
/// The members a type inherits from its base types that are visible outside their assembly.
/// It is used to find the members that hide an inherited member (<c>new</c> in C#), as this is not recorded in metadata.
/// </summary>
/// <remarks>
/// Types are compared using a canonical name, so both the reflection and the metadata readers get the same result:
/// the metadata full name of the type, <c>!0</c> for the generic parameters of the type that declares the hiding member
/// and <c>!!0</c> for the generic parameters of a method.
/// </remarks>
internal sealed class InheritedMemberSet
{
    private readonly Dictionary<string, List<(InheritedMemberKind Kind, int Arity, string Parameters)>> _members = new(StringComparer.Ordinal);

    public void Add(InheritedMemberKind kind, string name, int arity = 0, string parameters = "")
    {
        if (!_members.TryGetValue(name, out var members))
        {
            members = [];
            _members.Add(name, members);
        }

        members.Add((kind, arity, parameters));
    }

    public bool ContainsName(string name) => _members.ContainsKey(name);

    // Same rules as the C# compiler:
    // - A constant, field, property, event, or type hides all base class members with the same name
    // - A method hides all non-method base class members with the same name, and all base class methods with the same signature
    // - An indexer hides all base class indexers with the same signature
    // The return type is not part of the signature, and members of different arity do not hide each other,
    // except a generic method which also hides the non-generic members that are not methods.
    public bool IsHiddenBy(InheritedMemberKind kind, string name, int arity = 0, string parameters = "")
    {
        if (!_members.TryGetValue(name, out var members))
            return false;

        foreach (var member in members)
        {
            if (member.Kind != kind)
            {
                if (member.Arity == arity || (kind == InheritedMemberKind.Method && member.Arity == 0))
                    return true;

                continue;
            }

            var isHidden = kind switch
            {
                InheritedMemberKind.Method => member.Arity == arity && string.Equals(member.Parameters, parameters, StringComparison.Ordinal),
                InheritedMemberKind.Property => string.Equals(member.Parameters, parameters, StringComparison.Ordinal),
                InheritedMemberKind.Type => member.Arity == arity,
                _ => true,
            };

            if (isHidden)
                return true;
        }

        return false;
    }

    public static string GetPrimitiveTypeName(PrimitiveTypeCode typeCode)
    {
        return typeCode switch
        {
            PrimitiveTypeCode.Boolean => "System.Boolean",
            PrimitiveTypeCode.Byte => "System.Byte",
            PrimitiveTypeCode.SByte => "System.SByte",
            PrimitiveTypeCode.Char => "System.Char",
            PrimitiveTypeCode.Int16 => "System.Int16",
            PrimitiveTypeCode.UInt16 => "System.UInt16",
            PrimitiveTypeCode.Int32 => "System.Int32",
            PrimitiveTypeCode.UInt32 => "System.UInt32",
            PrimitiveTypeCode.Int64 => "System.Int64",
            PrimitiveTypeCode.UInt64 => "System.UInt64",
            PrimitiveTypeCode.Single => "System.Single",
            PrimitiveTypeCode.Double => "System.Double",
            PrimitiveTypeCode.IntPtr => "System.IntPtr",
            PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
            PrimitiveTypeCode.Object => "System.Object",
            PrimitiveTypeCode.String => "System.String",
            PrimitiveTypeCode.TypedReference => "System.TypedReference",
            PrimitiveTypeCode.Void => "System.Void",
            _ => throw new ArgumentOutOfRangeException(nameof(typeCode), typeCode, message: null),
        };
    }

    public static string GetGenericTypeParameterName(int index) => "!" + index.ToString(CultureInfo.InvariantCulture);

    public static string GetGenericMethodParameterName(int index) => "!!" + index.ToString(CultureInfo.InvariantCulture);

    public static string GetGenericInstantiationName(string genericTypeName, IEnumerable<string> typeArguments) => genericTypeName + "<" + string.Join(',', typeArguments) + ">";

    public static string GetArrayName(string elementTypeName, int rank, bool isSZArray)
    {
        if (isSZArray)
            return elementTypeName + "[]";

        return elementTypeName + "[" + (rank == 1 ? "*" : new string(',', rank - 1)) + "]";
    }

    public static string GetByReferenceName(string elementTypeName) => elementTypeName + "&";

    public static string GetPointerName(string elementTypeName) => elementTypeName + "*";

    public static string GetFunctionPointerName(string returnTypeName, IEnumerable<string> parameterTypeNames, bool isUnmanaged)
    {
        return (isUnmanaged ? "delegate* unmanaged<" : "delegate*<") + string.Join(',', parameterTypeNames.Append(returnTypeName)) + ">";
    }

    // The compiler does not make a difference between in and ref readonly parameters when it looks for hidden members
    public static string GetParameterName(string typeName, bool isByReference, bool isOut, bool isReadOnly)
    {
        if (!isByReference)
            return typeName;

        return (isOut ? "out " : isReadOnly ? "in " : "ref ") + typeName;
    }

    public static string GetParameterList(IEnumerable<string> parameterNames) => string.Join(',', parameterNames);

    // The destructor of a base type is not hidden by a method named Finalize
    public static bool IsDestructor(string name, bool isStatic, int parameterCount) => !isStatic && parameterCount == 0 && string.Equals(name, "Finalize", StringComparison.Ordinal);

    public static bool IsReadOnlyReferenceAttribute(string? attributeTypeFullName)
    {
        return attributeTypeFullName is "System.Runtime.CompilerServices.IsReadOnlyAttribute" or "System.Runtime.CompilerServices.RequiresLocationAttribute";
    }
}
