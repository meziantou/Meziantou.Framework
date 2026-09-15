namespace Meziantou.Framework.Yaml.SourceGeneration;

internal enum DictionaryKind
{
    Dictionary,
    IDictionary,
    IReadOnlyDictionary,
    OrderedDictionary,

    /// <summary>A class with a public parameterless constructor implementing <c>IDictionary&lt;TKey, TValue&gt;</c>, such as <c>SortedDictionary&lt;TKey, TValue&gt;</c>.</summary>
    MutableDictionary,

    /// <summary><c>FrozenDictionary&lt;TKey, TValue&gt;</c>, read as a <c>Dictionary&lt;TKey, TValue&gt;</c> that is then frozen.</summary>
    FrozenDictionary,
}
