namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlIListConverter<TElement> : YamlConverter<IList<TElement>?>
{
    private YamlConverter? _elementConverter;

    public override bool CanPopulate(Type typeToConvert) => typeToConvert == typeof(IList<TElement>);

    public override object? Populate(YamlReader reader, Type typeToConvert, object existingValue)
    {
        ArgumentNullException.ThrowIfNull(existingValue);
        if (existingValue is not IList<TElement> list)
        {
            throw new InvalidOperationException($"Existing value for '{typeToConvert}' must implement '{typeof(IList<TElement>)}'.");
        }

        return SequenceReadHelpers.PopulateCollection(reader, list, ref _elementConverter, "IList");
    }

    public override IList<TElement>? Read(YamlReader reader)
    {
        var list = SequenceReadHelpers.ReadList<TElement>(reader, ref _elementConverter, "IList");
        return list;
    }

    public override void Write(YamlWriter writer, IList<TElement>? value)
    {
        SequenceReadHelpers.WriteEnumerable(writer, value, ref _elementConverter);
    }
}
