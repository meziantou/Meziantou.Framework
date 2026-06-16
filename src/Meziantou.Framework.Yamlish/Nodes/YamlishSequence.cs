using System.Collections;

namespace Meziantou.Framework.Yamlish.Nodes;

public sealed class YamlishSequence : YamlishNode, IReadOnlyList<YamlishNode>
{
    private readonly List<YamlishNode> _items = [];

    public YamlishSequence()
    {
    }

    public YamlishSequence(IEnumerable<YamlishNode> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items)
        {
            Add(item);
        }
    }

    public override YamlishNodeKind Kind => YamlishNodeKind.Sequence;

    public int Count => _items.Count;

    public YamlishNode this[int index] => _items[index];

    public void Add(YamlishNode item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
    }

    public IEnumerator<YamlishNode> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
