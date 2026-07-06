namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlICollectionConverter<TElement> : YamlConverter<ICollection<TElement>?>
{
    private YamlConverter? _elementConverter;

    public override bool CanPopulate(Type typeToConvert) => typeToConvert == typeof(ICollection<TElement>);

    public override object? Populate(YamlReader reader, Type typeToConvert, object existingValue)
    {
        ArgumentNullException.ThrowIfNull(existingValue);
        if (existingValue is not ICollection<TElement> collection)
        {
            throw new InvalidOperationException($"Existing value for '{typeToConvert}' must implement '{typeof(ICollection<TElement>)}'.");
        }

        return SequenceReadHelpers.PopulateCollection(reader, collection, ref _elementConverter, "ICollection");
    }

    public override ICollection<TElement>? Read(YamlReader reader)
    {
        var list = SequenceReadHelpers.ReadList<TElement>(reader, ref _elementConverter, "ICollection");
        return list;
    }

    public override void Write(YamlWriter writer, ICollection<TElement>? value)
    {
        SequenceReadHelpers.WriteEnumerable(writer, value, ref _elementConverter);
    }
}
