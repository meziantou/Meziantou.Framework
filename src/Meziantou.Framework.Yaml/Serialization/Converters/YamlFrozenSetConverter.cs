using System.Collections.Frozen;

namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlFrozenSetConverter<TElement> : YamlConstructedSequenceConverter<FrozenSet<TElement>?, TElement>
{
    protected override string DisplayName => "FrozenSet";

    protected override FrozenSet<TElement> Create(List<TElement> elements) => elements.ToFrozenSet();

    protected override bool IsNull(FrozenSet<TElement>? value) => value is null;

    protected override IEnumerable<TElement> GetElements(FrozenSet<TElement>? value) => value!;
}
