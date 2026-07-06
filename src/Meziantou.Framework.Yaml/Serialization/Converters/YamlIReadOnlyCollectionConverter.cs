namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlIReadOnlyCollectionConverter<TElement> : YamlConverter<IReadOnlyCollection<TElement>?>
{
    private YamlConverter? _elementConverter;

    public override IReadOnlyCollection<TElement>? Read(YamlReader reader)
    {
        var list = SequenceReadHelpers.ReadList<TElement>(reader, ref _elementConverter, "IReadOnlyCollection");
        return list;
    }

    public override void Write(YamlWriter writer, IReadOnlyCollection<TElement>? value)
    {
        SequenceReadHelpers.WriteEnumerable(writer, value, ref _elementConverter);
    }
}
