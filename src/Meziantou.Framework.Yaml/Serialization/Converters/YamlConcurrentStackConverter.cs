using System.Collections.Concurrent;

namespace Meziantou.Framework.Yaml.Serialization.Converters;

/// <remarks>A stack is enumerated from its top, so the elements are pushed back in reverse order.</remarks>
internal sealed class YamlConcurrentStackConverter<TElement> : YamlConstructedSequenceConverter<ConcurrentStack<TElement>?, TElement>
{
    protected override string DisplayName => "ConcurrentStack";

    protected override ConcurrentStack<TElement> Create(List<TElement> elements)
    {
        elements.Reverse();
        return new(elements);
    }

    protected override bool IsNull(ConcurrentStack<TElement>? value) => value is null;

    protected override IEnumerable<TElement> GetElements(ConcurrentStack<TElement>? value) => value!;
}
