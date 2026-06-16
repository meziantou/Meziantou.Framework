using System.Collections;

namespace Meziantou.Framework.Yamlish.Nodes;

/// <summary>Represents a Yamlish sequence node.</summary>
public sealed class YamlishSequence : YamlishNode, IReadOnlyList<YamlishNode>
{
    private readonly List<YamlishNode> _items = [];

    /// <summary>Initializes a new instance of the <see cref="YamlishSequence" /> class.</summary>
    public YamlishSequence()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="YamlishSequence" /> class with the specified items.</summary>
    /// <param name="items">The sequence items.</param>
    public YamlishSequence(IEnumerable<YamlishNode> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items)
        {
            Add(item);
        }
    }

    /// <inheritdoc />
    public override YamlishNodeKind Kind => YamlishNodeKind.Sequence;

    /// <summary>Gets or sets the style used when writing the sequence.</summary>
    public YamlishSequenceStyle Style { get; set; }

    /// <inheritdoc />
    public int Count => _items.Count;

    /// <inheritdoc />
    public YamlishNode this[int index] => _items[index];

    /// <summary>Adds a node to the sequence.</summary>
    /// <param name="item">The node to add.</param>
    public void Add(YamlishNode item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
    }

    /// <inheritdoc />
    public IEnumerator<YamlishNode> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
