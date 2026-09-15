using System.Collections.Concurrent;

namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlConcurrentBagConverter<TElement> : YamlConstructedSequenceConverter<ConcurrentBag<TElement>?, TElement>
{
    protected override string DisplayName => "ConcurrentBag";

    protected override ConcurrentBag<TElement> Create(List<TElement> elements) => new(elements);

    protected override bool IsNull(ConcurrentBag<TElement>? value) => value is null;

    protected override IEnumerable<TElement> GetElements(ConcurrentBag<TElement>? value) => value!;
}
