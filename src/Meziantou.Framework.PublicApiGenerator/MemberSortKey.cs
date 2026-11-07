namespace Meziantou.Framework.PublicApiGenerator;

// The position of a member among the members of its declaring type
internal sealed class MemberSortKey
{
    private readonly MemberSortKind _kind;
    private readonly string _comparedName;
    private readonly string _name;
    private readonly string _qualifier;
    private readonly int _genericParameterCount;
    private readonly int _parameterCount;
    private readonly string _signature;
    private readonly string _returnType;

    /// <param name="kind">The kind of the member.</param>
    /// <param name="metadataName">The name of the member in metadata. It is empty for a constructor.</param>
    /// <param name="genericParameterCount">The number of generic parameters of a method.</param>
    /// <param name="parameterCount">The number of parameters of a method or of an indexer.</param>
    /// <param name="signature">The parameters of a method or of an indexer: the modifier (<c>ref</c>, <c>in</c>...) and the type of each parameter, separated by a comma.</param>
    /// <param name="returnType">The return type of a method. Conversion operators can only differ by their return type.</param>
    public MemberSortKey(MemberSortKind kind, string metadataName, int genericParameterCount = 0, int parameterCount = 0, string signature = "", string returnType = "")
    {
        _kind = kind;
        (_comparedName, _name, _qualifier) = PublicApiOrdering.SplitMemberName(metadataName);
        _genericParameterCount = genericParameterCount;
        _parameterCount = parameterCount;
        _signature = signature;
        _returnType = returnType;
    }

    public static Comparer<MemberSortKey> Comparer { get; } = Comparer<MemberSortKey>.Create(Compare);

    public static int Compare(MemberSortKey x, MemberSortKey y)
    {
        var result = x._kind.CompareTo(y._kind);
        if (result != 0)
            return result;

        result = string.CompareOrdinal(x._comparedName, y._comparedName);
        if (result != 0)
            return result;

        result = string.CompareOrdinal(x._name, y._name);
        if (result != 0)
            return result;

        result = string.CompareOrdinal(x._qualifier, y._qualifier);
        if (result != 0)
            return result;

        result = x._genericParameterCount.CompareTo(y._genericParameterCount);
        if (result != 0)
            return result;

        result = x._parameterCount.CompareTo(y._parameterCount);
        if (result != 0)
            return result;

        result = string.CompareOrdinal(x._signature, y._signature);
        if (result != 0)
            return result;

        return string.CompareOrdinal(x._returnType, y._returnType);
    }
}
