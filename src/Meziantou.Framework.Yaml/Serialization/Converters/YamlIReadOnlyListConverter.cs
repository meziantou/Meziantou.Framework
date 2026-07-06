namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlIReadOnlyListConverter<TElement> : YamlConverter<IReadOnlyList<TElement>?>
{
    private YamlConverter? _elementConverter;

    public override IReadOnlyList<TElement>? Read(YamlReader reader)
    {
        var list = SequenceReadHelpers.ReadList<TElement>(reader, ref _elementConverter, "IReadOnlyList");
        return list;
    }

    public override void Write(YamlWriter writer, IReadOnlyList<TElement>? value)
    {
        SequenceReadHelpers.WriteEnumerable(writer, value, ref _elementConverter);
    }
}
