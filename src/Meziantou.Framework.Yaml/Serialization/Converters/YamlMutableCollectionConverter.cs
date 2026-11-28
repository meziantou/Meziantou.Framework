using System.Collections.ObjectModel;

namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlMutableCollectionConverter<TCollection, TElement> : YamlConverter<TCollection?>
    where TCollection : class, ICollection<TElement>, new()
{
    private YamlConverter? _elementConverter;

    public override bool CanPopulate(Type typeToConvert) => typeToConvert == typeof(TCollection);

    public override object? Populate(YamlReader reader, Type typeToConvert, object existingValue)
    {
        ArgumentNullException.ThrowIfNull(existingValue);
        if (existingValue is not ICollection<TElement> collection)
        {
            throw new InvalidOperationException($"Existing value for '{typeToConvert}' must implement '{typeof(ICollection<TElement>)}'.");
        }

        return SequenceReadHelpers.PopulateCollection(reader, collection, ref _elementConverter, typeof(TCollection).Name);
    }

    public override TCollection? Read(YamlReader reader)
    {
        if (reader.TryReadAlias(out var rootAliasValue))
        {
            return (TCollection)rootAliasValue!;
        }

        if (reader.TokenType == YamlTokenType.Alias)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, $"Aliases are not supported when deserializing into {typeof(TCollection)} unless ReferenceHandling is Preserve.");
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

        // A Collection<T> adds an item to its list through two virtual calls: the list is filled instead. A type that derives
        // from it can override how an item is added, so it is given its items one at a time.
        TCollection collection;
        List<TElement>? list = null;
        if (typeof(TCollection) == typeof(Collection<TElement>))
        {
            list = [];
            collection = (TCollection)(object)new Collection<TElement>(list);
        }
        else
        {
            collection = new TCollection();
        }

        if (reader.ReferenceReader is not null && anchor is not null)
        {
            reader.ReferenceReader.Register(anchor, collection);
        }

        var typedElementConverter = _elementConverter as YamlConverter<TElement>;
        while (reader.TokenType != YamlTokenType.EndSequence)
        {
            var element = SequenceReadHelpers.ReadElement(reader, typedElementConverter, _elementConverter);
            if (list is not null)
            {
                list.Add(element);
            }
            else
            {
                collection.Add(element);
            }
        }

        reader.Read();
        return collection;
    }

    public override void Write(YamlWriter writer, TCollection? value)
    {
        SequenceReadHelpers.WriteEnumerable(writer, value, ref _elementConverter);
    }
}
