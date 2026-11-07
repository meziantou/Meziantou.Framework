namespace Meziantou.Framework.Yaml.Serialization.Converters;

/// <remarks>Only the elements of the segment are written, and a segment is read over a new array.</remarks>
internal sealed class YamlArraySegmentConverter<TElement> : YamlConstructedSequenceConverter<ArraySegment<TElement>, TElement>
{
    protected override string DisplayName => "ArraySegment";

    protected override ArraySegment<TElement> Create(List<TElement> elements) => new(elements.ToArray());

    protected override bool IsNull(ArraySegment<TElement> value) => value.Array is null;

    protected override IEnumerable<TElement> GetElements(ArraySegment<TElement> value) => value;
}
