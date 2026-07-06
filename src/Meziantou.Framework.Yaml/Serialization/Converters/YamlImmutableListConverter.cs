using System.Collections.Immutable;

namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlImmutableListConverter<TElement> : YamlConverter<ImmutableList<TElement>?>
{
    private YamlConverter? _elementConverter;

    public override ImmutableList<TElement>? Read(YamlReader reader)
    {
        if (reader.TryReadAlias(out var rootAliasValue))
        {
            return (ImmutableList<TElement>)rootAliasValue!;
        }

        if (reader.TokenType == YamlTokenType.Alias)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, "Aliases are not supported when deserializing into ImmutableList unless ReferenceHandling is Preserve.");
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

        var builder = ImmutableList.CreateBuilder<TElement>();
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

    public override void Write(YamlWriter writer, ImmutableList<TElement>? value)
    {
        SequenceReadHelpers.WriteEnumerable(writer, value, ref _elementConverter);
    }
}
