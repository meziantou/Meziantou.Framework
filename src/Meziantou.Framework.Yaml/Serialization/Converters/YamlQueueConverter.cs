namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlQueueConverter<TElement> : YamlConstructedSequenceConverter<Queue<TElement>?, TElement>
{
    protected override string DisplayName => "Queue";

    protected override Queue<TElement> Create(List<TElement> elements) => new(elements);

    protected override bool IsNull(Queue<TElement>? value) => value is null;

    protected override IEnumerable<TElement> GetElements(Queue<TElement>? value) => value!;
}
