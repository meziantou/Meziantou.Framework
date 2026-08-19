namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlIReadOnlySetConverter<TElement> : YamlConverter<IReadOnlySet<TElement>?>
{
    private YamlConverter? _elementConverter;

    public override IReadOnlySet<TElement>? Read(YamlReader reader)
    {
        if (reader.TryReadAlias(out var rootAliasValue))
        {
            return (IReadOnlySet<TElement>)rootAliasValue!;
        }

        if (reader.TokenType == YamlTokenType.Alias)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, "Aliases are not supported when deserializing into a set unless ReferenceHandling is Preserve.");
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

        var set = new HashSet<TElement>();
        if (reader.ReferenceReader is not null && anchor is not null)
        {
            reader.ReferenceReader.Register(anchor, set);
        }

        while (reader.TokenType != YamlTokenType.EndSequence)
        {
            var value = _elementConverter.Read(reader, typeof(TElement));
            set.Add((TElement)value!);
        }

        reader.Read();
        return set;
    }

    public override void Write(YamlWriter writer, IReadOnlySet<TElement>? value)
    {
        SequenceReadHelpers.WriteEnumerable(writer, value, ref _elementConverter);
    }
}
