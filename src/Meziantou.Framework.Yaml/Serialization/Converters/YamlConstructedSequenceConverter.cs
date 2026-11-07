namespace Meziantou.Framework.Yaml.Serialization.Converters;

/// <summary>
/// Converts a sequence type that cannot be filled through <see cref="ICollection{T}"/>: the elements are read into a
/// list, from which the collection is then created.
/// </summary>
/// <remarks>
/// The elements are written in enumeration order. The source generator emits the same shape, so both serializers
/// produce and accept the same YAML.
/// </remarks>
internal abstract class YamlConstructedSequenceConverter<TCollection, TElement> : YamlConverter<TCollection>
{
    private YamlConverter? _elementConverter;

    protected abstract string DisplayName { get; }

    protected abstract TCollection Create(List<TElement> elements);

    protected abstract bool IsNull(TCollection value);

    protected abstract IEnumerable<TElement> GetElements(TCollection value);

    public override TCollection? Read(YamlReader reader)
    {
        if (reader.TryReadAlias(out var rootAliasValue))
        {
            return (TCollection)rootAliasValue!;
        }

        if (reader.TokenType == YamlTokenType.Alias)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, $"Aliases are not supported when deserializing into {DisplayName} unless ReferenceHandling is Preserve.");
        }

        if (reader.TokenType == YamlTokenType.Scalar && YamlScalar.IsNull(reader))
        {
            reader.Read();
            return default;
        }

        if (reader.TokenType != YamlTokenType.StartSequence)
        {
            throw YamlThrowHelper.ThrowExpectedSequence(reader);
        }

        _elementConverter ??= reader.GetConverter(typeof(TElement));
        var anchor = reader.Anchor;
        reader.Read();

        var elements = new List<TElement>();
        while (reader.TokenType != YamlTokenType.EndSequence)
        {
            elements.Add((TElement)_elementConverter.Read(reader, typeof(TElement))!);
        }

        reader.Read();
        var result = Create(elements);
        if (anchor is not null && result is not null)
        {
            reader.RegisterAnchor(anchor, result);
        }

        return result;
    }

    public override void Write(YamlWriter writer, TCollection value)
    {
        if (IsNull(value))
        {
            writer.WriteNullValue();
            return;
        }

        _elementConverter ??= writer.GetConverter(typeof(TElement));
        if (!typeof(TCollection).IsValueType && writer.TryWriteReference(value))
        {
            return;
        }

        writer.WriteStartSequence();
        foreach (var element in GetElements(value))
        {
            _elementConverter.Write(writer, element);
        }

        writer.WriteEndSequence();
    }
}
