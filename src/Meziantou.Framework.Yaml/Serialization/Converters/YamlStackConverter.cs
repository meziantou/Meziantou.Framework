namespace Meziantou.Framework.Yaml.Serialization.Converters;

/// <remarks>A stack is enumerated from its top, so the elements are pushed back in reverse order.</remarks>
internal sealed class YamlStackConverter<TElement> : YamlConstructedSequenceConverter<Stack<TElement>?, TElement>
{
    protected override string DisplayName => "Stack";

    protected override Stack<TElement> Create(List<TElement> elements)
    {
        elements.Reverse();
        return new(elements);
    }

    protected override bool IsNull(Stack<TElement>? value) => value is null;

    protected override IEnumerable<TElement> GetElements(Stack<TElement>? value) => value!;
}
