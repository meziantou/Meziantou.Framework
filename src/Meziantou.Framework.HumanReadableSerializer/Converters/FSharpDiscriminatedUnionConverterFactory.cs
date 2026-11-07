using System.Diagnostics;
using Meziantou.Framework.HumanReadable.Utils;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class FSharpDiscriminatedUnionConverterFactory : HumanReadableConverterFactory
{
    public override bool CanConvert(Type type)
    {
        var utils = FSharpUtils.Get(type);
        return utils?.IsUnionType(type) is true;
    }

    public override HumanReadableConverter? CreateConverter(Type typeToConvert, HumanReadableSerializerOptions options)
    {
        // Both probes swallow reflection failures and return null, so they cannot be trusted
        // to succeed here just because CanConvert did.
        var info = FSharpUtils.Get(typeToConvert)
            ?? throw new HumanReadableSerializerException($"Cannot serialize the F# union type '{typeToConvert}' as the F# reflection API is not available");

        var unionCases = info.GetUnionCases(typeToConvert)
            ?? throw new HumanReadableSerializerException($"Cannot serialize the F# union type '{typeToConvert}' as its union cases cannot be determined");

        var tagProperty = typeToConvert.GetProperty("Tag")
            ?? throw new HumanReadableSerializerException($"Cannot serialize the F# union type '{typeToConvert}' as its union case cannot be determined");

        // The fields go through the same member options as the members of an object (ignore conditions, names, order, converters)
        var cases = new Dictionary<int, (string Name, HumanReadableMemberInfo[] Members)>();
        foreach (var unionCase in unionCases)
        {
            cases[unionCase.Tag] = (unionCase.Name ?? "", HumanReadableMemberInfo.Get(typeToConvert, unionCase.GetFields(), options));
        }

        return new FSharpDiscriminatedUnionConverter(typeToConvert, tagProperty, cases);
    }

    private sealed class FSharpDiscriminatedUnionConverter(Type unionType, System.Reflection.PropertyInfo tagProperty, Dictionary<int, (string Name, HumanReadableMemberInfo[] Members)> cases) : HumanReadableConverter
    {
        public override bool CanConvert(Type type) => type == unionType;

        public override void WriteValue(HumanReadableTextWriter writer, object? value, Type valueType, HumanReadableSerializerOptions options)
        {
            Debug.Assert(value is not null);

            if (tagProperty.GetValue(value) is not int tag || !cases.TryGetValue(tag, out var unionCase))
                throw new HumanReadableSerializerException($"Cannot serialize the F# union type '{unionType}' as its union case cannot be determined");

            writer.StartObject();
            writer.WritePropertyName("Tag");
            writer.WriteValue(unionCase.Name);

            foreach (var member in unionCase.Members)
            {
                if (!member.TryGetValue(value, out var memberValue))
                    continue;

                writer.WritePropertyName(member.Name);
                member.WriteValue(writer, memberValue, options);
            }

            writer.EndObject();
        }
    }
}
