namespace Meziantou.Framework.Pdf.TestHarness.Arlington;

/// <summary>A row of a TSV file of the Arlington PDF Model: a key and its alternatives of types.</summary>
public sealed class ArlingtonKey
{
    private ArlingtonKey(string name, IReadOnlyList<ArlingtonType> types, bool? required, bool inheritable)
    {
        Name = name;
        Types = types;
        Required = required;
        Inheritable = inheritable;
    }

    /// <summary>Gets the key: a name, an index, <c>*</c> for any key, or an index followed by <c>*</c> for the elements of a repeating group.</summary>
    public string Name { get; }

    /// <summary>Gets the types the value may have.</summary>
    public IReadOnlyList<ArlingtonType> Types { get; }

    /// <summary>Gets a value indicating whether the key is required: <see langword="null"/> when it depends on a predicate the validator does not evaluate.</summary>
    public bool? Required { get; }

    /// <summary>Gets a value indicating whether the value can be inherited from a parent (the page tree).</summary>
    public bool Inheritable { get; }

    /// <summary>Gets a value indicating whether the key is a wildcard (<c>*</c>).</summary>
    public bool IsWildcard => Name == "*";

    /// <summary>Gets a value indicating whether the key is an element of a repeating group of an array (<c>0*</c>).</summary>
    public bool IsRepeating => Name.Length > 1 && Name[^1] == '*';

    internal static ArlingtonKey Parse(string[] columns)
    {
        // Key, Type, SinceVersion, DeprecatedIn, Required, IndirectReference, Inheritable, DefaultValue, PossibleValues, SpecialCase, Link, Note
        var typeNames = ArlingtonSyntax.SplitAlternatives(columns[1]);
        var indirect = ArlingtonSyntax.SplitPerType(columns[5], typeNames.Count);
        var possibleValues = ArlingtonSyntax.SplitPerType(columns[8], typeNames.Count);
        var links = ArlingtonSyntax.SplitPerType(columns[10], typeNames.Count);
        var types = new List<ArlingtonType>(typeNames.Count);
        for (var i = 0; i < typeNames.Count; i++)
        {
            var typeName = ArlingtonSyntax.Unwrap(typeNames[i]);
            types.Add(new ArlingtonType(
                typeName,
                ParseIndirect(indirect[i]),
                [.. ArlingtonSyntax.SplitList(possibleValues[i])],
                [.. ArlingtonSyntax.SplitList(links[i]).Select(ArlingtonSyntax.Unwrap).Where(l => l.Length > 0)]));
        }

        bool? required = columns[4] switch
        {
            "TRUE" => true,
            "FALSE" => false,
            _ => null,
        };
        return new ArlingtonKey(columns[0], types, required, columns[6] == "TRUE");
    }

    private static ArlingtonIndirect ParseIndirect(string value)
    {
        return value switch
        {
            "TRUE" or "[TRUE]" => ArlingtonIndirect.MustBeIndirect,
            "FALSE" or "[FALSE]" or "" => ArlingtonIndirect.Any,
            "fn:MustBeDirect()" or "[fn:MustBeDirect()]" => ArlingtonIndirect.MustBeDirect,
            _ => ArlingtonIndirect.Any,
        };
    }
}
