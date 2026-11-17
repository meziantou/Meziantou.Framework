using System.Runtime.CompilerServices;

namespace Meziantou.Framework.WebPlatform.Html;

internal static partial class TagNames
{
    /// <summary>
    /// An open-addressing hash table of the tag ids, indexed by a hash of the length and of three characters of the name: a
    /// lookup computes the hash and compares the name with one or two candidates (faster than a dictionary for these short names).
    /// </summary>
    private static class Lookup
    {
        private const int Bits = 10;
        private const int Mask = (1 << Bits) - 1;

        private static readonly TagId[] Table = CreateTable();

        public static int MaxLength { get; } = Names.Max(name => name.Length);

        private static TagId[] CreateTable()
        {
            var table = new TagId[1 << Bits];
            for (var i = 1; i < Names.Length; i++)
            {
                var index = GetIndex(Names[i]);
                while (table[index] != TagId.Unknown)
                {
                    index = (index + 1) & Mask;
                }

                table[index] = (TagId)i;
            }

            return table;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetIndex(ReadOnlySpan<char> name)
        {
            var hash = ((uint)name.Length * 31 + name[0]) * 31 + name[name.Length >> 1];
            hash = (hash * 31 + name[^1]) * 0x9E3779B1;
            return (int)(hash >> (32 - Bits));
        }

        public static TagId Find(string name)
        {
            if ((uint)(name.Length - 1) >= (uint)MaxLength)
                return TagId.Unknown;

            var index = GetIndex(name);
            while (true)
            {
                var id = Table[index];
                if (id == TagId.Unknown)
                    return TagId.Unknown;

                var candidate = Names[(int)id];
                if (ReferenceEquals(candidate, name) || string.Equals(candidate, name, StringComparison.Ordinal))
                    return id;

                index = (index + 1) & Mask;
            }
        }

        public static TagId Find(ReadOnlySpan<char> name)
        {
            if ((uint)(name.Length - 1) >= (uint)MaxLength)
                return TagId.Unknown;

            var index = GetIndex(name);
            while (true)
            {
                var id = Table[index];
                if (id == TagId.Unknown)
                    return TagId.Unknown;

                if (name.SequenceEqual(Names[(int)id]))
                    return id;

                index = (index + 1) & Mask;
            }
        }
    }

    public static TagId GetTagId(string name) => Lookup.Find(name);

    public static TagId GetTagId(ReadOnlySpan<char> name) => Lookup.Find(name);

    /// <summary>Returns the interned string for a well-known tag name, or null.</summary>
    public static string? GetInternedName(ReadOnlySpan<char> name) => Lookup.Find(name) is var id and not TagId.Unknown ? Names[(int)id] : null;

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
