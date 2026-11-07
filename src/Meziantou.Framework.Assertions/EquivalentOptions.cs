namespace Meziantou.Framework.Assertions;

public sealed class EquivalentOptions
{
    /// <summary>Gets or sets a value indicating whether collections, including JSON arrays, match regardless of the order of their items.</summary>
    public bool IgnoreCollectionOrder { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether members whose names differ only by case are paired. Members with the same
    /// name are paired first, so a type whose member names differ only by case still has all of them compared.
    /// </summary>
    public bool IgnoreMemberNameCase { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether strings are compared ignoring case. It also applies to dictionary keys,
    /// JSON strings and property names, and the content of a <see cref="System.Text.StringBuilder"/>.
    /// </summary>
    public bool IgnoreStringCase { get; set; }
}
