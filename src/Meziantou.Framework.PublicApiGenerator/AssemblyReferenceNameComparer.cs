namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>Sorts assembly names without case, so that <c>netstandard</c> is next to the other names starting with <c>n</c>, then ordinally so that the order is stable.</summary>
internal sealed class AssemblyReferenceNameComparer : IComparer<string>
{
    public static AssemblyReferenceNameComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        var result = StringComparer.OrdinalIgnoreCase.Compare(x, y);
        if (result is not 0)
            return result;

        return StringComparer.Ordinal.Compare(x, y);
    }
}
