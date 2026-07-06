using System.Collections.ObjectModel;
using Meziantou.Framework.Yaml.Tokens;

namespace Meziantou.Framework.Yaml;

/// <summary>
/// Collection of <see cref="TagDirective"/>.
/// </summary>
public class TagDirectiveCollection : KeyedCollection<string, TagDirective>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TagDirectiveCollection"/> class.
    /// </summary>
    public TagDirectiveCollection()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TagDirectiveCollection"/> class.
    /// </summary>
    /// <param name="tagDirectives">Initial content of the collection.</param>
    public TagDirectiveCollection(IEnumerable<TagDirective> tagDirectives)
    {
        foreach (var tagDirective in tagDirectives)
        {
            Add(tagDirective);
        }
    }

    /// <inheritdoc/>
    protected override string GetKeyForItem(TagDirective item) => item.Handle;

    /// <summary>Gets a value indicating whether the collection contains a directive with the same handle</summary>
    public new bool Contains(TagDirective directive) => Contains(GetKeyForItem(directive));
}