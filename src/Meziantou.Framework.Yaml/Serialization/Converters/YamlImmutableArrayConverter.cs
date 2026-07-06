using System.Collections.Immutable;

namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlImmutableArrayConverter<TElement> : YamlConverter<ImmutableArray<TElement>>
{
    private YamlConverter? _elementConverter;

    public override ImmutableArray<TElement> Read(YamlReader reader)
    {
        if (reader.TryReadAlias(out var rootAliasValue))
        {
            return (ImmutableArray<TElement>)rootAliasValue!;
        }

        if (reader.TokenType == YamlTokenType.Alias)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, "Aliases are not supported when deserializing into ImmutableArray unless ReferenceHandling is Preserve.");
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
        var rootAnchor = reader.Anchor;
        reader.Read();

        var builder = ImmutableArray.CreateBuilder<TElement>();
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

    public override void Write(YamlWriter writer, ImmutableArray<TElement> value)
    {
        if (value.IsDefault)
        {
            writer.WriteNullValue();
            return;
        }

        _elementConverter ??= writer.GetConverter(typeof(TElement));

        writer.WriteStartSequence();
        for (var i = 0; i < value.Length; i++)
        {
            _elementConverter.Write(writer, value[i]);
        }
        writer.WriteEndSequence();
    }
}
