using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace Meziantou.Framework.Tds.Protocol;

/// <summary>The COLLATION structure that precedes the value of a non-Unicode character parameter.</summary>
/// <remarks>
/// <c>char</c>, <c>varchar</c> and <c>text</c> values are encoded with the code page of their collation, so they
/// cannot be decoded without it. The structure is a 32-bit word holding the LCID, the comparison flags and the
/// collation version, followed by a sort id.
/// </remarks>
internal static class TdsCollation
{
    /// <summary>The wire size of a COLLATION structure.</summary>
    public const int Size = 5;

    private const uint LcidMask = 0x000FFFFF;

    /// <summary>Marks the UTF-8 collations introduced by SQL Server 2019.</summary>
    private const uint Utf8Flag = 0x04000000;

    private static readonly ConcurrentDictionary<int, Encoding?> EncodingsByCodePage = new();

    /// <summary>
    /// Returns the encoding the collation names, or <see langword="null"/> when it names a code page this
    /// runtime cannot decode.
    /// </summary>
    public static Encoding? TryGetEncoding(ReadOnlySpan<byte> collation)
    {
        var info = BinaryPrimitives.ReadUInt32LittleEndian(collation);
        var sortId = collation[4];

        // A UTF-8 collation keeps the LCID of the Windows collation it derives from, so the flag decides first.
        if ((info & Utf8Flag) != 0)
        {
            return Encoding.UTF8;
        }

        var codePage = sortId is 0 ? GetCodePageFromLcid(info & LcidMask) : GetCodePageFromSortId(sortId);
        if (codePage is 0)
        {
            return null;
        }

        // The code pages a collation can name are not built into the runtime, and registering the provider
        // process-wide would change how every other library in the process resolves encodings.
        return EncodingsByCodePage.GetOrAdd(codePage, static codePage => CodePagesEncodingProvider.Instance.GetEncoding(codePage));
    }

    /// <summary>Returns the code page of a SQL collation, or 0 when the sort id is not one SQL Server defines.</summary>
    private static int GetCodePageFromSortId(byte sortId)
    {
        return sortId switch
        {
            >= 30 and <= 34 => 437,
            (>= 40 and <= 44) or 49 or (>= 55 and <= 61) => 850,
            (>= 50 and <= 54) or (>= 71 and <= 75) or (>= 183 and <= 186) or (>= 210 and <= 217) => 1252,
            >= 80 and <= 98 => 1250,
            >= 104 and <= 108 => 1251,
            (>= 112 and <= 114) or (>= 120 and <= 122) or 124 => 1253,
            >= 128 and <= 130 => 1254,
            >= 136 and <= 138 => 1255,
            >= 144 and <= 146 => 1256,
            >= 152 and <= 160 => 1257,
            192 or 193 or 200 => 932,
            194 or 195 or 201 => 949,
            196 or 197 or 202 => 950,
            198 or 199 or 203 => 936,
            >= 204 and <= 206 => 874,
            _ => 0,
        };
    }

    /// <summary>Returns the ANSI code page of a Windows collation, or 0 when the LCID cannot be resolved.</summary>
    private static int GetCodePageFromLcid(uint lcid)
    {
        if (TryGetAnsiCodePage(lcid, out var codePage))
        {
            return codePage;
        }

        // Alternate sort orders (German phone book, Chinese stroke count, ...) set the bits above the locale
        // id, and they share the code page of the locale they sort differently.
        return TryGetAnsiCodePage(lcid & 0xFFFF, out codePage) ? codePage : 0;
    }

    private static bool TryGetAnsiCodePage(uint lcid, out int codePage)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo((int)lcid);

            // Invariant globalization resolves every locale id to the invariant culture, whose code page says
            // nothing about the collation. An unknown id can also land on the custom-culture placeholder.
            if (culture.LCID == (int)lcid)
            {
                codePage = culture.TextInfo.ANSICodePage;
                return codePage is not 0;
            }
        }
        catch (CultureNotFoundException)
        {
        }

        codePage = 0;
        return false;
    }
}
