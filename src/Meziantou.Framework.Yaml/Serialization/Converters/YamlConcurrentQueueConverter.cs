using System.Collections.Concurrent;

namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlConcurrentQueueConverter<TElement> : YamlConstructedSequenceConverter<ConcurrentQueue<TElement>?, TElement>
{
    protected override string DisplayName => "ConcurrentQueue";

    protected override ConcurrentQueue<TElement> Create(List<TElement> elements) => new(elements);

    protected override bool IsNull(ConcurrentQueue<TElement>? value) => value is null;

    protected override IEnumerable<TElement> GetElements(ConcurrentQueue<TElement>? value) => value!;
}
