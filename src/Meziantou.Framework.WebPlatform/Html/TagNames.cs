using System.Collections.Frozen;

namespace Meziantou.Framework.WebPlatform.Html;

internal static partial class TagNames
{
    private static class Lookup
    {
        public static readonly FrozenDictionary<string, TagId> Map = CreateMap();
        public static readonly FrozenDictionary<string, TagId>.AlternateLookup<ReadOnlySpan<char>> SpanLookup = Map.GetAlternateLookup<ReadOnlySpan<char>>();

        private static FrozenDictionary<string, TagId> CreateMap()
        {
            var dictionary = new Dictionary<string, TagId>(Count, StringComparer.Ordinal);
            for (var i = 1; i < Names.Length; i++)
            {
                dictionary.Add(Names[i], (TagId)i);
            }

            return dictionary.ToFrozenDictionary(StringComparer.Ordinal);
        }
    }

    public static TagId GetTagId(string name) => Lookup.Map.TryGetValue(name, out var id) ? id : TagId.Unknown;

    public static TagId GetTagId(ReadOnlySpan<char> name) => Lookup.SpanLookup.TryGetValue(name, out var id) ? id : TagId.Unknown;

    /// <summary>Returns the interned string for a well-known tag name, or null.</summary>
    public static string? GetInternedName(ReadOnlySpan<char> name) => Lookup.SpanLookup.TryGetValue(name, out var id) ? Names[(int)id] : null;

    public static string GetName(TagId id) => Names[(int)id];

    /// <summary>Returns the tag name in ASCII uppercase (the tagName of an HTML element in an HTML document).</summary>
    public static string GetUppercaseName(TagId id) => Uppercase.Names[(int)id];

    private static class Uppercase
    {
        public static readonly string[] Names = CreateNames();

        private static string[] CreateNames()
        {
            var names = new string[TagNames.Names.Length];
            for (var i = 0; i < names.Length; i++)
            {
                names[i] = Infra.InfraAscii.ToAsciiUppercase(TagNames.Names[i]);
            }

            return names;
        }
    }
}
