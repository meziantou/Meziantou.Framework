using System.Collections.ObjectModel;

namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal static class SequenceReadHelpers
{
    /// <summary>Reads an element with its converter, without boxing it when the converter is typed.</summary>
    public static TElement ReadElement<TElement>(YamlReader reader, YamlConverter converter)
    {
        if (converter is YamlConverter<TElement> typedConverter)
            return typedConverter.Read(reader)!;

        return (TElement)converter.Read(reader, typeof(TElement))!;
    }

    /// <summary>
    /// Reads an element with <paramref name="typedConverter"/>, which is <paramref name="converter"/> when it is typed: the
    /// loops that read the elements of a sequence cast the converter once, instead of once per element.
    /// </summary>
    public static TElement ReadElement<TElement>(YamlReader reader, YamlConverter<TElement>? typedConverter, YamlConverter converter)
        => typedConverter is not null ? typedConverter.Read(reader)! : (TElement)converter.Read(reader, typeof(TElement))!;

    /// <summary>Reads a sequence into a new <see cref="List{T}"/> exposed as <typeparamref name="TCollection"/>.</summary>
    /// <remarks>
    /// An alias resolves to the object of its anchor, which is not necessarily a list: an array aliased by an
    /// <see cref="IList{T}"/> member is assigned as is, and an object that is not a <typeparamref name="TCollection"/> is
    /// reported as a <see cref="YamlException"/>.
    /// </remarks>
    public static TCollection? ReadList<TCollection, TElement>(YamlReader reader, ref YamlConverter? elementConverter, string typeDisplayName)
        where TCollection : class, IEnumerable<TElement>
    {
        if (reader.TryReadAlias(out var rootAliasValue))
        {
            return YamlThrowHelper.CastAliasValue<TCollection>(reader, rootAliasValue);
        }

        if (reader.TokenType == YamlTokenType.Alias)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, $"Aliases are not supported when deserializing into {typeDisplayName} unless ReferenceHandling is Preserve.");
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

        elementConverter ??= reader.GetConverter(typeof(TElement));
        var anchor = reader.Anchor;
        reader.Read();

        var list = new List<TElement>();
        if (reader.ReferenceReader is not null && anchor is not null)
        {
            reader.ReferenceReader.Register(anchor, list);
        }

        var typedElementConverter = elementConverter as YamlConverter<TElement>;
        while (reader.TokenType != YamlTokenType.EndSequence)
        {
            list.Add(ReadElement(reader, typedElementConverter, elementConverter));
        }

        reader.Read();
        return (TCollection)(object)list;
    }

    public static void WriteEnumerable<TElement>(YamlWriter writer, IEnumerable<TElement>? value, ref YamlConverter? elementConverter)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        elementConverter ??= writer.GetConverter(typeof(TElement));

        // The runtime type decides whether the value is tracked: a boxed value type is not an identity worth preserving.
        if (writer.TryWriteReference(value))
        {
            return;
        }

        writer.WriteStartSequence();

        // The common collections are enumerated without boxing their enumerator, and the elements are written by the typed
        // converter without being boxed
        var typedElementConverter = elementConverter as YamlConverter<TElement>;
        switch (value)
        {
            case List<TElement> list:
                foreach (var item in list)
                {
                    WriteElement(writer, typedElementConverter, elementConverter, item);
                }

                break;

            case TElement[] array:
                foreach (var item in array)
                {
                    WriteElement(writer, typedElementConverter, elementConverter, item);
                }

                break;

            case Collection<TElement> collection:
                for (var i = 0; i < collection.Count; i++)
                {
                    WriteElement(writer, typedElementConverter, elementConverter, collection[i]);
                }

                break;

            default:
                foreach (var item in value)
                {
                    WriteElement(writer, typedElementConverter, elementConverter, item);
                }

                break;
        }

        writer.WriteEndSequence();
    }

    /// <summary>Writes an element with <paramref name="typedConverter"/>, which is <paramref name="converter"/> when it is typed.</summary>
    public static void WriteElement<TElement>(YamlWriter writer, YamlConverter<TElement>? typedConverter, YamlConverter converter, TElement value)
    {
        if (typedConverter is not null)
        {
            typedConverter.Write(writer, value);
        }
        else
        {
            converter.Write(writer, value);
        }
    }

    public static ICollection<TElement>? PopulateCollection<TElement>(YamlReader reader, ICollection<TElement> collection, ref YamlConverter? elementConverter, string typeDisplayName)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(collection);

        if (reader.TryReadAlias(out var rootAliasValue))
        {
            return (ICollection<TElement>)rootAliasValue!;
        }

        if (reader.TokenType == YamlTokenType.Alias)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, $"Aliases are not supported when deserializing into {typeDisplayName} unless ReferenceHandling is Preserve.");
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

        if (reader.ReferenceReader is not null && reader.Anchor is not null)
        {
            reader.ReferenceReader.Register(reader.Anchor, collection);
        }

        elementConverter ??= reader.GetConverter(typeof(TElement));
        reader.Read();

        var typedElementConverter = elementConverter as YamlConverter<TElement>;
        while (reader.TokenType != YamlTokenType.EndSequence)
        {
            collection.Add(ReadElement(reader, typedElementConverter, elementConverter));
        }

        reader.Read();
        return collection;
    }
}
