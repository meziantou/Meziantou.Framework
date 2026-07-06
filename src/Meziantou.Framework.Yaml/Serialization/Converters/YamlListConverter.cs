namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlListConverter<TElement> : YamlConverter<List<TElement>?>
{
    private YamlConverter? _elementConverter;

    public override bool CanPopulate(Type typeToConvert) => typeToConvert == typeof(List<TElement>);

    public override object? Populate(YamlReader reader, Type typeToConvert, object existingValue)
    {
        ArgumentNullException.ThrowIfNull(existingValue);
        if (existingValue is not List<TElement> list)
        {
            throw new InvalidOperationException($"Existing value for '{typeToConvert}' must be a '{typeof(List<TElement>)}'.");
        }

        return SequenceReadHelpers.PopulateCollection(reader, list, ref _elementConverter, "List");
    }

    public override List<TElement>? Read(YamlReader reader)
    {
        if (reader.TryReadAlias(out var rootAliasValue))
        {
            return (List<TElement>)rootAliasValue!;
        }

        if (reader.TokenType == YamlTokenType.Alias)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, "Aliases are not supported when deserializing into a list unless ReferenceHandling is Preserve.");
        }

        if (reader.TokenType == YamlTokenType.Scalar && YamlScalar.IsNull(reader))
        {
            reader.Read();
            return null;
        }

        if (reader.TokenType != YamlTokenType.StartSequence)
        {
            throw YamlThrowHelper.ThrowExpectedSequence(reader);
        }

        _elementConverter ??= reader.GetConverter(typeof(TElement));
        var anchor = reader.Anchor;
        reader.Read();

        var list = new List<TElement>();
        if (reader.ReferenceReader is not null && anchor is not null)
        {
            reader.ReferenceReader.Register(anchor, list);
        }

        while (reader.TokenType != YamlTokenType.EndSequence)
        {
            var value = _elementConverter.Read(reader, typeof(TElement));
            list.Add((TElement)value!);
        }

        reader.Read();
        return list;
    }

    public override void Write(YamlWriter writer, List<TElement>? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        _elementConverter ??= writer.GetConverter(typeof(TElement));

        if (writer.ReferenceWriter is not null)
        {
            if (writer.ReferenceWriter.TryGetAnchor(value, out var existing))
            {
                writer.WriteAlias(existing);
                return;
            }

            var anchor = writer.ReferenceWriter.GetOrAddAnchor(value);
            writer.WriteAnchor(anchor);
        }

        writer.WriteStartSequence();
        for (var i = 0; i < value.Count; i++)
        {
            _elementConverter.Write(writer, value[i]);
        }
        writer.WriteEndSequence();
    }
}
