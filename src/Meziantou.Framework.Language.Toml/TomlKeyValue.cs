namespace Meziantou.Framework.Language.Toml;

/// <summary>A key/value pair of a document, with the full key it defines: the header it is under, its own key, and the inline tables it is in.</summary>
/// <remarks>
/// <para>
/// In <c>[dependencies.tokio]</c> followed by <c>version = "1.0"</c>, the pair <c>version = "1.0"</c> has the names
/// <c>dependencies</c>, <c>tokio</c>, and <c>version</c>, as it would in <c>dependencies.tokio.version = "1.0"</c> or
/// in <c>tokio = { version = "1.0" }</c> under <c>[dependencies]</c>.
/// </para>
/// <para>
/// The key/value pairs of every table of an array of tables have the same names: <see cref="Table"/> tells them apart.
/// </para>
/// </remarks>
public sealed class TomlKeyValue
{
    internal TomlKeyValue(TomlTableSyntax? table, IReadOnlyList<string> names, IReadOnlyList<SyntaxToken> parts, TomlPropertySyntax property)
    {
        Table = table;
        Names = names;
        Parts = parts;
        Property = property;
    }

    /// <summary>Gets the header the key/value pair is under, or <see langword="null"/> for one of the root table.</summary>
    public TomlTableSyntax? Table { get; }

    /// <summary>Gets the name of each part of the full key, with quotes removed and escape sequences resolved.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>Gets the token that writes each of <see cref="Names"/>, in the header, the key, or the key of an inline table.</summary>
    public IReadOnlyList<SyntaxToken> Parts { get; }

    /// <summary>Gets the key/value pair, which is in an inline table when the value is.</summary>
    public TomlPropertySyntax Property { get; }

    /// <summary>Gets the value.</summary>
    public TomlValueSyntax Value => Property.Value;

    public override string ToString() => string.Join('.', Names) + " = " + Value;

    /// <summary>A list made of two others, so that the pairs under a header share its names rather than each copying them.</summary>
    internal sealed class Concatenation<T>(IReadOnlyList<T> first, IReadOnlyList<T> second) : IReadOnlyList<T>
    {
        public int Count { get; } = first.Count + second.Count;

        public T this[int index] => index < first.Count ? first[index] : second[index - first.Count];

        public IEnumerator<T> GetEnumerator()
        {
            for (var i = 0; i < Count; i++)
            {
                yield return this[i];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
