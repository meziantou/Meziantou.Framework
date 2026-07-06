using System.Collections.Immutable;

namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlImmutableHashSetConverter<TElement> : YamlConverter<ImmutableHashSet<TElement>?>
{
    private YamlConverter? _elementConverter;

    public override ImmutableHashSet<TElement>? Read(YamlReader reader)
    {
        if (reader.TryReadAlias(out var rootAliasValue))
        {
            return (ImmutableHashSet<TElement>)rootAliasValue!;
        }

        if (reader.TokenType == YamlTokenType.Alias)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, "Aliases are not supported when deserializing into an immutable set unless ReferenceHandling is Preserve.");
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
        var rootAnchor = reader.Anchor;
        reader.Read();

        var builder = ImmutableHashSet.CreateBuilder<TElement>();
        while (reader.TokenType != YamlTokenType.EndSequence)
        {
            var value = _elementConverter.Read(reader, typeof(TElement));
            builder.Add((TElement)value!);
        }

        reader.Read();
        var result = builder.ToImmutable();
        if (rootAnchor is not null)
        {
            reader.RegisterAnchor(rootAnchor, result);
        }

        return result;
    }

    public override void Write(YamlWriter writer, ImmutableHashSet<TElement>? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        _elementConverter ??= writer.GetConverter(typeof(TElement));

        writer.WriteStartSequence();
        foreach (var item in value)
        {
            _elementConverter.Write(writer, item);
        }
        writer.WriteEndSequence();
    }
}
