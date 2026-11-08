using System.Collections.Frozen;

namespace Meziantou.Framework.Barcodes.Internal.Gs1;

// A port of the reference linters of the GS1 Barcode Syntax Dictionary (https://github.com/gs1/gs1-syntax-dictionary,
// Apache License 2.0, see THIRD-PARTY-NOTICES.md). Each linter checks the content of one component of an element string
// and reports the first error with the position and the length of the faulty data, as the reference implementation does.
internal static class Gs1Linters
{
    // The shortest GS1 Company Prefix has four digits
    private const int GcpMinLength = 4;

    private static readonly ulong[] Cset82Bitfield = [0x0000000067ffffff, 0x7fffffe17fffffe0, 0, 0];
    private static readonly ulong[] Cset39Bitfield = [0x000000001005ffc0, 0x7fffffe000000000, 0, 0];
    private static readonly ulong[] Cset64Bitfield = [0x000000000004ffc0, 0x7fffffe17fffffe0, 0, 0];

    private static readonly ulong[] Iso3166Bitfield =
    [
        0x08a888898888b888, 0x8aa80a2888888888, 0x0888888a22232889, 0x88188a2221e322a2,
        0x2a2a180088888888, 0x888a888888888888, 0x888288a2622a22a2, 0x222222228808b888,
        0x8888970808222222, 0x2de102888888a222, 0x320a1b222222a203, 0xe20808c8088888a8,
        0x8888889a89002021, 0xe080222a00082102, 0x0000000000000000, 0x0000000000000000,
    ];

    private static readonly ulong[] Iso4217Bitfield =
    [
        0x008800008808b808, 0x8880082080880808, 0x0880808800220008, 0x8010820202822000,
        0x0202000008000000, 0x8888088888888080, 0x088a88a262222002, 0x022200008800a080,
        0x88080c0008220200, 0x2022028880000020, 0x1202000000202002, 0xa200008000088888,
        0x0088880081002020, 0x2080002800002200, 0x0400000fe6adbfdf, 0xfdfdfce225000000,
    ];

    private static readonly ulong[] Iso3166Alpha2Bitfield =
    [
        0x1e9afb77f7bdbb7b, 0xe4fc21a8012b0070, 0x003a900df9dfa800, 0xb160181ef00202c0,
        0x00b8d42f8281f2bf, 0x3fffeba4d2100080, 0x023cf1ca80000002, 0x008a8fbfe75cddf9,
        0x59820820eaa10200, 0x4002000000000800, 0x1020020080000000,
    ];

    // AIDC media types: 01-10 (ICCBBA assignments) and 80-99 (ICCBBA local or national use)
    private static readonly byte[] MediaTypeBitfield = [0x7f, 0xe0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xff, 0xff, 0xf0];

    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> PackageTypes = FrozenSet.Create(StringComparer.Ordinal,
    [
        "1A", "1B", "1D", "1F", "1G", "1W",
        "200", "201", "202", "203", "204", "205", "206", "210", "211", "212", "2C",
        "3A", "3H",
        "43", "44", "4A", "4B", "4C", "4D", "4F", "4G", "4H",
        "5H", "5L", "5M",
        "6H", "6P",
        "7A", "7B",
        "8", "8A", "8B", "8C",
        "9",
        "AA", "AB", "AC", "AD", "AF", "AG", "AH", "AI", "AJ", "AL", "AM", "AP", "APE", "AT", "AV",
        "B4", "BB", "BC", "BD", "BE", "BF", "BG", "BGE", "BH", "BI", "BJ", "BK", "BL", "BM", "BME", "BN", "BO", "BP", "BQ", "BR", "BRI", "BS", "BT", "BU", "BV", "BW", "BX", "BY", "BZ",
        "CA", "CB", "CBL", "CC", "CCE", "CD", "CE", "CF", "CG", "CH", "CI", "CJ", "CK", "CL", "CM", "CN", "CO", "CP", "CQ", "CR", "CS", "CT", "CU", "CV", "CW", "CX", "CY", "CZ",
        "DA", "DB", "DC", "DG", "DH", "DI", "DJ", "DK", "DL", "DM", "DN", "DP", "DPE", "DR", "DS", "DT", "DU", "DV", "DW", "DX", "DY",
        "E1", "E2", "E3", "EC", "ED", "EE", "EF", "EG", "EH", "EI", "EN",
        "FB", "FC", "FD", "FE", "FI", "FL", "FO", "FOB", "FP", "FPE", "FR", "FT", "FW", "FX",
        "GB", "GI", "GL", "GR", "GU", "GY", "GZ",
        "HA", "HB", "HC", "HG", "HN", "HR",
        "IA", "IB", "IC", "ID", "IE", "IF", "IG", "IH", "IK", "IL", "IN", "IZ",
        "JB", "JC", "JG", "JR", "JT", "JY",
        "KG", "KI",
        "LAB", "LE", "LG", "LT", "LU", "LV", "LZ",
        "MA", "MB", "MC", "ME", "MPE", "MR", "MS", "MT", "MW", "MX",
        "NA", "NE", "NF", "NG", "NS", "NT", "NU", "NV",
        "OA", "OB", "OC", "OD", "OE", "OF", "OK", "OPE", "OT", "OU",
        "P2", "PA", "PAE", "PB", "PC", "PD", "PE", "PF", "PG", "PH", "PI", "PJ", "PK", "PL", "PLP", "PN", "PO", "POP", "PP", "PPE", "PR", "PT", "PU", "PUE", "PV", "PX", "PY", "PZ",
        "QA", "QB", "QC", "QD", "QF", "QG", "QH", "QJ", "QK", "QL", "QM", "QN", "QP", "QQ", "QR", "QS",
        "RB1", "RB2", "RB3", "RCB", "RD", "RG", "RJ", "RK", "RL", "RO", "RT", "RZ",
        "S1", "SA", "SB", "SC", "SD", "SE", "SEC", "SH", "SI", "SK", "SL", "SM", "SO", "SP", "SS", "ST", "STL", "SU", "SV", "SW", "SX", "SY", "SZ",
        "T1", "TB", "TC", "TD", "TE", "TEV", "TG", "THE", "TI", "TK", "TL", "TN", "TO", "TR", "TRE", "TS", "TT", "TTE", "TU", "TV", "TW", "TWE", "TY", "TZ",
        "UC", "UN", "UUE",
        "VA", "VG", "VI", "VK", "VL", "VN", "VO", "VP", "VQ", "VR", "VS", "VY",
        "WA", "WB", "WC", "WD", "WF", "WG", "WH", "WJ", "WK", "WL", "WM", "WN", "WP", "WQ", "WR", "WRP", "WS", "WT", "WU", "WV", "WW", "WX", "WY", "WZ",
        "X11", "X12", "X15", "X16", "X17", "X18", "X19", "X20", "X3", "XA", "XB", "XC", "XD", "XF", "XG", "XH", "XJ", "XK",
        "YA", "YB", "YC", "YD", "YF", "YG", "YH", "YJ", "YK", "YL", "YM", "YN", "YP", "YQ", "YR", "YS", "YT", "YV", "YW", "YX", "YY", "YZ",
        "ZA", "ZB", "ZC", "ZD", "ZF", "ZG", "ZH", "ZJ", "ZK", "ZL", "ZM", "ZN", "ZP", "ZQ", "ZR", "ZS", "ZT", "ZU", "ZV", "ZW", "ZX", "ZY", "ZZ",
    ]).GetAlternateLookup<ReadOnlySpan<char>>();

    public static bool IsKnown(string name) => GetLinter(name) is not null;

    public static Gs1LintResult Lint(string name, ReadOnlySpan<char> data)
    {
        var linter = GetLinter(name) ?? throw new ArgumentException($"Unknown linter '{name}'.", nameof(name));
        return linter(data);
    }

    // The year used to choose the century of a two-digit year (GS1 General Specifications, section 7.12)
    private static int CurrentYear => DateTime.UtcNow.Year;

    private delegate Gs1LintResult Linter(ReadOnlySpan<char> data);

    private static Linter? GetLinter(string name)
    {
        return name switch
        {
            "csetnumeric" => CsetNumeric,
            "cset82" => Cset82,
            "cset39" => Cset39,
            "cset64" => Cset64,
            "csum" => Csum,
            "csumalpha" => CsumAlpha,
            "gcppos1" => GcpPos1,
            "gcppos2" => GcpPos2,
            "couponcode" => CouponCode,
            "couponposoffer" => CouponPosOffer,
            "hasnondigit" => HasNonDigit,
            "hh" => Hour,
            "mi" => Minute,
            "ss" => Second,
            "hhmi" => HourMinute,
            "hyphen" => Hyphen,
            "iban" => Iban,
            "importeridx" => ImporterIndex,
            "iso3166" => Iso3166,
            "iso3166999" => Iso3166Or999,
            "iso3166alpha2" => Iso3166Alpha2,
            "iso4217" => Iso4217,
            "iso5218" => Iso5218,
            "latitude" => Latitude,
            "longitude" => Longitude,
            "mediatype" => MediaType,
            "nonzero" => NonZero,
            "nozeroprefix" => NoZeroPrefix,
            "packagetype" => PackageType,
            "pcenc" => PercentEncoding,
            "pieceoftotal" => PieceOfTotal,
            "posinseqslash" => PositionInSequence,
            "winding" => Winding,
            "yesno" => YesNo,
            "yymmd0" => Yymmd0,
            "yymmdd" => Yymmdd,
            "yyyymmd0" => Yyyymmd0,
            "yyyymmdd" => Yyyymmdd,
            "zero" => Zero,
            _ => null,
        };
    }

    private static Gs1LintResult Error(Gs1LintError error, int position, int length) => new(error, position, length);

    private static bool IsDigit(char c) => char.IsAsciiDigit(c);

    private static int Digit(char c) => c - '0';

    private static bool BitfieldLookup(ReadOnlySpan<ulong> field, int bit)
    {
        if (bit < 0 || bit / 64 >= field.Length)
            return false;

        return (field[bit / 64] & (1UL << 63 >> (bit % 64))) != 0;
    }

    private static bool BitfieldLookup(ReadOnlySpan<byte> field, int bit)
    {
        if (bit < 0 || bit / 8 >= field.Length)
            return false;

        return (field[bit / 8] & (0x80 >> (bit % 8))) != 0;
    }

    private static Gs1LintResult CheckCharacters(ReadOnlySpan<char> data, ReadOnlySpan<ulong> bitfield, Gs1LintError error)
    {
        for (var pos = 0; pos < data.Length; pos++)
        {
            if (!BitfieldLookup(bitfield, data[pos]))
                return Error(error, pos, 1);
        }

        return Gs1LintResult.Ok;
    }

    private static Gs1LintResult CheckDigits(ReadOnlySpan<char> data)
    {
        for (var pos = 0; pos < data.Length; pos++)
        {
            if (!IsDigit(data[pos]))
                return Error(Gs1LintError.NonDigitCharacter, pos, 1);
        }

        return Gs1LintResult.Ok;
    }

    private static Gs1LintResult CsetNumeric(ReadOnlySpan<char> data) => CheckDigits(data);

    private static Gs1LintResult Cset82(ReadOnlySpan<char> data) => CheckCharacters(data, Cset82Bitfield, Gs1LintError.InvalidCset82Character);

    private static Gs1LintResult Cset39(ReadOnlySpan<char> data) => CheckCharacters(data, Cset39Bitfield, Gs1LintError.InvalidCset39Character);

    private static Gs1LintResult Cset64(ReadOnlySpan<char> data)
    {
        var pads = 0;
        while (pads < data.Length && data[data.Length - pads - 1] == '=')
        {
            pads++;
        }

        var length = data.Length - pads;
        if (pads > 2 || (pads > 0 && data.Length % 3 != 0))
            return Error(Gs1LintError.InvalidCset64Padding, length, pads);

        return CheckCharacters(data[..length], Cset64Bitfield, Gs1LintError.InvalidCset64Character);
    }

    private static Gs1LintResult Csum(ReadOnlySpan<char> data)
    {
        if (data.IsEmpty)
            return Error(Gs1LintError.TooShortForCheckDigit, 0, 0);

        var weight = data.Length % 2 == 0 ? 3 : 1;
        var parity = 0;
        for (var pos = 0; pos < data.Length - 1; pos++)
        {
            if (!IsDigit(data[pos]))
                return Error(Gs1LintError.NonDigitCharacter, pos, 1);

            parity += weight * Digit(data[pos]);
            weight = 4 - weight;
        }

        var last = data.Length - 1;
        if (!IsDigit(data[last]))
            return Error(Gs1LintError.NonDigitCharacter, last, 1);

        parity = (10 - (parity % 10)) % 10;
        if (parity != Digit(data[last]))
            return Error(Gs1LintError.IncorrectCheckDigit, last, 1);

        return Gs1LintResult.Ok;
    }

    private static Gs1LintResult CsumAlpha(ReadOnlySpan<char> data)
    {
        ReadOnlySpan<ushort> primes =
        [
            2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71, 73, 79, 83, 89,
            97, 101, 103, 107, 109, 113, 127, 131, 137, 139, 149, 151, 157, 163, 167, 173, 179, 181, 191, 193, 197, 199, 211, 223,
            227, 229, 233, 239, 241, 251, 257, 263, 269, 271, 277, 281, 283, 293, 307, 311, 313, 317, 331, 337, 347, 349, 353, 359,
            367, 373, 379, 383, 389, 397, 401, 409, 419, 421, 431, 433, 439, 443, 449, 457, 461, 463, 467, 479, 487, 491, 499, 503,
            509,
        ];
        const string Cset32 = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

        if (data.Length > primes.Length + 2)
            return Error(Gs1LintError.TooLongForCheckPairImplementation, 0, data.Length);

        if (data.Length < 2)
            return Error(Gs1LintError.TooShortForCheckPair, 0, data.Length);

        if (data.Length == 2)
        {
            if (data[0] != '2' || data[1] != '2')
                return Error(Gs1LintError.IncorrectCheckPair, 0, 2);

            return Gs1LintResult.Ok;
        }

        var sum = 0;
        for (var pos = 0; pos < data.Length - 2; pos++)
        {
            var weight = GetCset82Weight(data[pos]);
            if (weight == 0)
                return Error(Gs1LintError.InvalidCset82Character, pos, 1);

            sum += (weight - 1) * primes[data.Length - 3 - pos];
        }

        sum %= 1021;
        if (data[^2] != Cset32[sum >> 5] || data[^1] != Cset32[sum & 31])
            return Error(Gs1LintError.IncorrectCheckPair, data.Length - 2, 2);

        return Gs1LintResult.Ok;

        // The position of the character in CSET 82, starting at 1; 0 for the other characters
        static int GetCset82Weight(char c)
        {
            const string Cset82 = "!\"%&'()*+,-./0123456789:;<=>?ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz";
            return Cset82.IndexOf(c, StringComparison.Ordinal) + 1;
        }
    }

    private static Gs1LintResult GcpPos1(ReadOnlySpan<char> data)
    {
        if (data.Length < GcpMinLength)
            return Error(Gs1LintError.TooShortForGcp, 0, data.Length);

        for (var i = 0; i < GcpMinLength; i++)
        {
            if (!IsDigit(data[i]))
                return Error(Gs1LintError.InvalidGcpPrefix, i, 1);
        }

        return Gs1LintResult.Ok;
    }

    private static Gs1LintResult GcpPos2(ReadOnlySpan<char> data)
    {
        if (data.Length < 2)
            return Error(Gs1LintError.TooShortForGcp, 0, data.Length);

        var result = GcpPos1(data[1..]);
        if (!result.IsValid)
            return result with { Position = result.Position + 1 };

        return Gs1LintResult.Ok;
    }

    private static Gs1LintResult Truncated(Gs1LintError error, int p, int q) => p == q ? Error(error, 0, q) : Error(error, p, q - p);

    private static Gs1LintResult Missing(Gs1LintError error, int q) => Error(error, 0, q);

    // Reads a "requirement" field: a VLI of 1 to 5, the value, a requirement code of 0 to 4 or 9 and a family code of 3 digits
    private static bool TryReadPurchaseRequirement(ReadOnlySpan<char> data, ref int p, int q, int index, out Gs1LintResult result)
    {
        var (missingVli, invalidVli, truncated, missingCode, invalidCode, truncatedFamily) = index switch
        {
            1 => (Gs1LintError.CouponMissing1stPurchaseRequirementVli, Gs1LintError.CouponInvalid1stPurchaseRequirementLength, Gs1LintError.CouponTruncated1stPurchaseRequirement, Gs1LintError.CouponMissing1stPurchaseRequirementCode, Gs1LintError.CouponInvalid1stPurchaseRequirementCode, Gs1LintError.CouponTruncated1stPurchaseFamilyCode),
            2 => (Gs1LintError.CouponMissing2ndPurchaseRequirementVli, Gs1LintError.CouponInvalid2ndPurchaseRequirementLength, Gs1LintError.CouponTruncated2ndPurchaseRequirement, Gs1LintError.CouponMissing2ndPurchaseRequirementCode, Gs1LintError.CouponInvalid2ndPurchaseRequirementCode, Gs1LintError.CouponTruncated2ndPurchaseFamilyCode),
            _ => (Gs1LintError.CouponMissing3rdPurchaseRequirementVli, Gs1LintError.CouponInvalid3rdPurchaseRequirementLength, Gs1LintError.CouponTruncated3rdPurchaseRequirement, Gs1LintError.CouponMissing3rdPurchaseRequirementCode, Gs1LintError.CouponInvalid3rdPurchaseRequirementCode, Gs1LintError.CouponTruncated3rdPurchaseFamilyCode),
        };

        if (p == q)
        {
            result = Missing(missingVli, q);
            return false;
        }

        if (data[p] is < '1' or > '5')
        {
            result = Error(invalidVli, p, 1);
            return false;
        }

        var vli = Digit(data[p]);
        p++;
        if (q - p < vli)
        {
            result = Truncated(truncated, p, q);
            return false;
        }

        p += vli;
        if (p == q)
        {
            result = Missing(missingCode, q);
            return false;
        }

        if (data[p] > '4' && data[p] != '9')
        {
            result = Error(invalidCode, p, 1);
            return false;
        }

        p++;
        if (q - p < 3)
        {
            result = Truncated(truncatedFamily, p, q);
            return false;
        }

        p += 3;
        result = Gs1LintResult.Ok;
        return true;
    }

    // Reads the GS1 Company Prefix of an additional purchase: a VLI of 0 to 6 (6 to 12 digits) or 9 (no prefix), then the prefix
    private static bool TryReadAdditionalPurchaseGcp(ReadOnlySpan<char> data, ref int p, int q, int index, out Gs1LintResult result)
    {
        var (missingVli, invalidVli, truncated) = index switch
        {
            2 => (Gs1LintError.CouponMissing2ndPurchaseGcpVli, Gs1LintError.CouponInvalid2ndPurchaseGcpLength, Gs1LintError.CouponTruncated2ndPurchaseGcp),
            _ => (Gs1LintError.CouponMissing3rdPurchaseGcpVli, Gs1LintError.CouponInvalid3rdPurchaseGcpLength, Gs1LintError.CouponTruncated3rdPurchaseGcp),
        };

        if (p == q)
        {
            result = Missing(missingVli, q);
            return false;
        }

        if (data[p] > '6' && data[p] != '9')
        {
            result = Error(invalidVli, p, 1);
            return false;
        }

        var vli = data[p] != '9' ? Digit(data[p]) + 6 : 0;
        p++;
        if (q - p < vli)
        {
            result = Truncated(truncated, p, q);
            return false;
        }

        if (vli > 0)
        {
            var gcp = GcpPos1(data.Slice(p, vli));
            if (!gcp.IsValid)
            {
                result = Error(gcp.Error, p, vli);
                return false;
            }

            p += vli;
        }

        result = Gs1LintResult.Ok;
        return true;
    }

    // North American coupon code (AI 8110), GS1 US "Coupon Code" specification
    private static Gs1LintResult CouponCode(ReadOnlySpan<char> data)
    {
        var digits = CheckDigits(data);
        if (!digits.IsValid)
            return digits;

        var p = 0;
        var q = data.Length;

        // Primary GS1 Company Prefix
        if (p == q)
            return Missing(Gs1LintError.CouponMissingGcpVli, q);

        if (data[p] > '6')
            return Error(Gs1LintError.CouponInvalidGcpLength, p, 1);

        var vli = Digit(data[p]) + 6;
        p++;
        if (q - p < vli)
            return Truncated(Gs1LintError.CouponTruncatedGcp, p, q);

        var gcp = GcpPos1(data.Slice(p, vli));
        if (!gcp.IsValid)
            return Error(gcp.Error, p, vli);

        p += vli;

        // Offer code
        if (q - p < 6)
            return Truncated(Gs1LintError.CouponTruncatedOfferCode, p, q);

        p += 6;

        // Save value
        if (p == q)
            return Missing(Gs1LintError.CouponMissingSaveValueVli, q);

        if (data[p] is < '1' or > '5')
            return Error(Gs1LintError.CouponInvalidSaveValueLength, p, 1);

        vli = Digit(data[p]);
        p++;
        if (q - p < vli)
            return Truncated(Gs1LintError.CouponTruncatedSaveValue, p, q);

        p += vli;

        // Primary purchase requirement
        if (!TryReadPurchaseRequirement(data, ref p, q, 1, out var result))
            return result;

        // Optional field 1: second qualifying purchase
        if (p < q && data[p] == '1')
        {
            p++;
            if (p == q)
                return Missing(Gs1LintError.CouponMissingAdditionalPurchaseRulesCode, q);

            if (data[p] > '3')
                return Error(Gs1LintError.CouponInvalidAdditionalPurchaseRulesCode, p, 1);

            p++;
            if (!TryReadPurchaseRequirement(data, ref p, q, 2, out result))
                return result;

            if (!TryReadAdditionalPurchaseGcp(data, ref p, q, 2, out result))
                return result;
        }

        // Optional field 2: third qualifying purchase
        if (p < q && data[p] == '2')
        {
            p++;
            if (!TryReadPurchaseRequirement(data, ref p, q, 3, out result))
                return result;

            if (!TryReadAdditionalPurchaseGcp(data, ref p, q, 3, out result))
                return result;
        }

        // Optional field 3: expiration date
        var expiryDate = -1;
        if (p < q && data[p] == '3')
        {
            p++;
            if (q - p < 6)
                return Truncated(Gs1LintError.CouponTooShortForExpirationDate, p, q);

            if (!Yymmdd(data.Slice(p, 6)).IsValid)
                return Error(Gs1LintError.CouponInvalidExpirationDate, p, 6);

            expiryDate = p;
            p += 6;
        }

        // Optional field 4: start date
        if (p < q && data[p] == '4')
        {
            p++;
            if (q - p < 6)
                return Truncated(Gs1LintError.CouponTooShortForStartDate, p, q);

            if (!Yymmdd(data.Slice(p, 6)).IsValid)
                return Error(Gs1LintError.CouponInvalidStartDate, p, 6);

            if (expiryDate >= 0 && data.Slice(p, 6).SequenceCompareTo(data.Slice(expiryDate, 6)) > 0)
                return Error(Gs1LintError.CouponExpirationBeforeStart, p - 8, 14);

            p += 6;
        }

        // Optional field 5: serial number
        if (p < q && data[p] == '5')
        {
            p++;
            if (p == q)
                return Missing(Gs1LintError.CouponMissingSerialNumberVli, q);

            vli = Digit(data[p]) + 6;
            p++;
            if (q - p < vli)
                return Truncated(Gs1LintError.CouponTruncatedSerialNumber, p, q);

            p += vli;
        }

        // Optional field 6: retailer GS1 Company Prefix or GLN
        if (p < q && data[p] == '6')
        {
            p++;
            if (p == q)
                return Missing(Gs1LintError.CouponMissingRetailerGcpOrGlnVli, q);

            if (data[p] is < '1' or > '7')
                return Error(Gs1LintError.CouponInvalidRetailerGcpOrGlnLength, p, 1);

            vli = Digit(data[p]) + 6;
            p++;
            if (q - p < vli)
                return Truncated(Gs1LintError.CouponTruncatedRetailerGcpOrGln, p, q);

            gcp = GcpPos1(data.Slice(p, vli));
            if (!gcp.IsValid)
                return Error(gcp.Error, p, vli);

            p += vli;
        }

        // Optional field 9: miscellaneous
        if (p < q && data[p] == '9')
        {
            p++;
            if (p == q)
                return Missing(Gs1LintError.CouponMissingSaveValueCode, q);

            if (data[p] is not ('0' or '1' or '2' or '5' or '6'))
                return Error(Gs1LintError.CouponInvalidSaveValueCode, p, 1);

            p++;
            if (p == q)
                return Missing(Gs1LintError.CouponMissingSaveValueAppliesToItem, q);

            if (data[p] > '2')
                return Error(Gs1LintError.CouponInvalidSaveValueAppliesToItem, p, 1);

            p++;
            if (p == q)
                return Missing(Gs1LintError.CouponMissingStoreCouponFlag, q);

            p++;
            if (p == q)
                return Missing(Gs1LintError.CouponMissingDontMultiplyFlag, q);

            if (data[p] is not ('0' or '1'))
                return Error(Gs1LintError.CouponInvalidDontMultiplyFlag, p, 1);

            p++;
        }

        if (p != q)
            return Error(Gs1LintError.CouponExcessData, p, q - p);

        return Gs1LintResult.Ok;
    }

    // Positive offer file coupon code (AI 8112)
    private static Gs1LintResult CouponPosOffer(ReadOnlySpan<char> data)
    {
        var digits = CheckDigits(data);
        if (!digits.IsValid)
            return digits;

        var p = 0;
        var q = data.Length;
        if (p == q)
            return Missing(Gs1LintError.CouponMissingFormatCode, q);

        if (data[p] is not ('0' or '1'))
            return Error(Gs1LintError.CouponInvalidFormatCode, p, 1);

        p++;
        if (p == q)
            return Missing(Gs1LintError.CouponMissingFunderVli, q);

        if (data[p] > '6')
            return Error(Gs1LintError.CouponInvalidFunderLength, p, 1);

        var vli = Digit(data[p]) + 6;
        p++;
        if (q - p < vli)
            return Truncated(Gs1LintError.CouponTruncatedFunder, p, q);

        p += vli;
        if (q - p < 6)
            return Truncated(Gs1LintError.CouponTruncatedOfferCode, p, q);

        p += 6;
        if (p == q)
            return Missing(Gs1LintError.CouponMissingSerialNumberVli, q);

        vli = Digit(data[p]) + 6;
        p++;
        if (q - p < vli)
            return Truncated(Gs1LintError.CouponTruncatedSerialNumber, p, q);

        p += vli;
        if (p != q)
            return Error(Gs1LintError.CouponExcessData, p, q - p);

        return Gs1LintResult.Ok;
    }

    private static Gs1LintResult HasNonDigit(ReadOnlySpan<char> data)
    {
        foreach (var c in data)
        {
            if (!IsDigit(c))
                return Gs1LintResult.Ok;
        }

        return Error(Gs1LintError.RequiresNonDigitCharacter, 0, data.Length);
    }

    private static Gs1LintResult TwoDigits(ReadOnlySpan<char> data, int max, Gs1LintError tooShort, Gs1LintError tooLong, Gs1LintError illegal)
    {
        if (data.Length != 2)
            return Error(data.Length < 2 ? tooShort : tooLong, 0, data.Length);

        for (var pos = 0; pos < 2; pos++)
        {
            if (!IsDigit(data[pos]))
                return Error(Gs1LintError.NonDigitCharacter, pos, 1);
        }

        if ((Digit(data[0]) * 10) + Digit(data[1]) > max)
            return Error(illegal, 0, 2);

        return Gs1LintResult.Ok;
    }

    private static Gs1LintResult Hour(ReadOnlySpan<char> data) => TwoDigits(data, 23, Gs1LintError.HourTooShort, Gs1LintError.HourTooLong, Gs1LintError.IllegalHour);

    private static Gs1LintResult Minute(ReadOnlySpan<char> data) => TwoDigits(data, 59, Gs1LintError.MinuteTooShort, Gs1LintError.MinuteTooLong, Gs1LintError.IllegalMinute);

    private static Gs1LintResult Second(ReadOnlySpan<char> data) => TwoDigits(data, 59, Gs1LintError.SecondTooShort, Gs1LintError.SecondTooLong, Gs1LintError.IllegalSecond);

    private static Gs1LintResult HourMinute(ReadOnlySpan<char> data)
    {
        if (data.Length != 4)
            return Error(data.Length < 4 ? Gs1LintError.HourWithMinuteTooShort : Gs1LintError.HourWithMinuteTooLong, 0, data.Length);

        var result = Hour(data[..2]);
        if (!result.IsValid)
            return result;

        result = Minute(data[2..]);
        if (!result.IsValid)
            return result with { Position = result.Position + 2 };

        return Gs1LintResult.Ok;
    }

    private static Gs1LintResult Hyphen(ReadOnlySpan<char> data)
    {
        if (data.IsEmpty)
            return Error(Gs1LintError.NotHyphen, 0, 0);

        foreach (var c in data)
        {
            if (c != '-')
                return Error(Gs1LintError.NotHyphen, 0, data.Length);
        }

        return Gs1LintResult.Ok;
    }

    private static Gs1LintResult Iban(ReadOnlySpan<char> data)
    {
        const int MinLength = 10;
        const int MaxLength = 34;

        if (data.Length < 4)
            return Error(Gs1LintError.IbanTooShort, 0, data.Length);

        if (!Iso3166Alpha2(data[..2]).IsValid)
            return Error(Gs1LintError.IllegalIbanCountryCode, 0, 2);

        if (data.Length > MaxLength)
            return Error(Gs1LintError.IbanTooLong, 0, data.Length);

        if (data.Length <= MinLength)
            return Error(Gs1LintError.IbanTooShort, 0, data.Length);

        // The check characters are computed with the first four characters moved to the end, letters counting for two digits
        var checksum = 0;
        for (var pos = 4; pos < data.Length + 4; pos++)
        {
            var actualPosition = pos < data.Length ? pos : pos - data.Length;
            var c = data[actualPosition];
            int value;
            if (IsDigit(c))
            {
                value = Digit(c);
                checksum = ((checksum * 10) + value) % 97;
            }
            else if (char.IsAsciiLetterUpper(c))
            {
                value = c - 'A' + 10;
                checksum = ((checksum * 100) + value) % 97;
            }
            else
            {
                return Error(Gs1LintError.InvalidIbanCharacter, actualPosition, 1);
            }
        }

        if (checksum != 1)
            return Error(Gs1LintError.IncorrectIbanChecksum, 2, 2);

        return Gs1LintResult.Ok;
    }

    private static Gs1LintResult ImporterIndex(ReadOnlySpan<char> data)
    {
        if (data.Length != 1)
            return Error(Gs1LintError.ImporterIdxMustBeOneCharacter, 0, data.Length);

        if (!BitfieldLookup(Cset64Bitfield, data[0]))
            return Error(Gs1LintError.InvalidImportIdxCharacter, 0, 1);

        return Gs1LintResult.Ok;
    }

    private static int ParseThreeDigits(ReadOnlySpan<char> data)
    {
        if (data.Length != 3 || !IsDigit(data[0]) || !IsDigit(data[1]) || !IsDigit(data[2]))
            return -1;

        return (Digit(data[0]) * 100) + (Digit(data[1]) * 10) + Digit(data[2]);
    }

    private static Gs1LintResult Iso3166(ReadOnlySpan<char> data)
    {
        if (!BitfieldLookup(Iso3166Bitfield, ParseThreeDigits(data)))
            return Error(Gs1LintError.NotIso3166, 0, data.Length);

        return Gs1LintResult.Ok;
    }

    private static Gs1LintResult Iso3166Or999(ReadOnlySpan<char> data)
    {
        if (data.SequenceEqual("999") || Iso3166(data).IsValid)
            return Gs1LintResult.Ok;

        return Error(Gs1LintError.NotIso3166Or999, 0, data.Length);
    }

    private static Gs1LintResult Iso3166Alpha2(ReadOnlySpan<char> data)
    {
        if (data.Length != 2 || !char.IsAsciiLetterUpper(data[0]) || !char.IsAsciiLetterUpper(data[1]) ||
            !BitfieldLookup(Iso3166Alpha2Bitfield, ((data[0] - 'A') * 26) + data[1] - 'A'))
        {
            return Error(Gs1LintError.NotIso3166Alpha2, 0, data.Length);
        }

        return Gs1LintResult.Ok;
    }

    private static Gs1LintResult Iso4217(ReadOnlySpan<char> data)
    {
        if (!BitfieldLookup(Iso4217Bitfield, ParseThreeDigits(data)))
            return Error(Gs1LintError.NotIso4217, 0, data.Length);

        return Gs1LintResult.Ok;
    }

    private static Gs1LintResult Iso5218(ReadOnlySpan<char> data)
    {
        if (data.Length != 1)
            return Error(Gs1LintError.InvalidBiologicalSexCode, 0, data.Length);

        if (data[0] is not ('0' or '1' or '2' or '9'))
            return Error(Gs1LintError.InvalidBiologicalSexCode, 0, 1);

        return Gs1LintResult.Ok;
    }

    private static Gs1LintResult Coordinate(ReadOnlySpan<char> data, long max, Gs1LintError invalidLength, Gs1LintError invalid)
    {
        if (data.Length != 10)
            return Error(invalidLength, 0, data.Length);

        long value = 0;
        for (var pos = 0; pos < data.Length; pos++)
        {
            if (!IsDigit(data[pos]))
                return Error(Gs1LintError.NonDigitCharacter, pos, 1);

            value = (value * 10) + Digit(data[pos]);
        }

        if (value > max)
            return Error(invalid, 0, 10);

        return Gs1LintResult.Ok;
    }

    private static Gs1LintResult Latitude(ReadOnlySpan<char> data) => Coordinate(data, 1_800_000_000, Gs1LintError.LatitudeInvalidLength, Gs1LintError.InvalidLatitude);

    private static Gs1LintResult Longitude(ReadOnlySpan<char> data) => Coordinate(data, 3_600_000_000, Gs1LintError.LongitudeInvalidLength, Gs1LintError.InvalidLongitude);

    private static Gs1LintResult MediaType(ReadOnlySpan<char> data)
    {
        var valid = data.Length == 2 && IsDigit(data[0]) && IsDigit(data[1]) && BitfieldLookup(MediaTypeBitfield, (Digit(data[0]) * 10) + Digit(data[1]));
        if (!valid)
            return Error(Gs1LintError.InvalidMediaType, 0, data.Length);

        return Gs1LintResult.Ok;
    }

    private static Gs1LintResult NonZero(ReadOnlySpan<char> data)
    {
        var hasNonZero = false;
        for (var pos = 0; pos < data.Length; pos++)
        {
            if (!IsDigit(data[pos]))
                return Error(Gs1LintError.NonDigitCharacter, pos, 1);

            if (data[pos] != '0')
            {
                hasNonZero = true;
            }
        }

        if (!hasNonZero)
            return Error(Gs1LintError.IllegalZeroValue, 0, data.Length);

        return Gs1LintResult.Ok;
    }

    private static Gs1LintResult NoZeroPrefix(ReadOnlySpan<char> data)
    {
        if (!data.IsEmpty && data[0] == '0')
            return Error(Gs1LintError.IllegalZeroPrefix, 0, 1);

        return CheckDigits(data);
    }

    private static Gs1LintResult PackageType(ReadOnlySpan<char> data)
    {
        if (!PackageTypes.Contains(data))
            return Error(Gs1LintError.InvalidPackageType, 0, data.Length);

        return Gs1LintResult.Ok;
    }

    private static Gs1LintResult PercentEncoding(ReadOnlySpan<char> data)
    {
        for (var pos = 0; pos < data.Length; pos++)
        {
            if (data[pos] != '%')
                continue;

            if (pos + 2 >= data.Length)
                return Error(Gs1LintError.InvalidPercentSequence, pos, data.Length - pos);

            if (!char.IsAsciiHexDigit(data[pos + 1]) || !char.IsAsciiHexDigit(data[pos + 2]))
                return Error(Gs1LintError.InvalidPercentSequence, pos, 3);

            pos += 2;
        }

        return Gs1LintResult.Ok;
    }

    private static Gs1LintResult PieceOfTotal(ReadOnlySpan<char> data)
    {
        var digits = CheckDigits(data);
        if (!digits.IsValid)
            return digits;

        if (data.IsEmpty || data.Length % 2 != 0)
            return Error(Gs1LintError.InvalidLengthForPieceOfTotal, 0, data.Length);

        var half = data.Length / 2;
        var piece = data[..half];
        var total = data[half..];
        if (!piece.ContainsAnyExcept('0'))
            return Error(Gs1LintError.ZeroPieceNumber, 0, half);

        if (!total.ContainsAnyExcept('0'))
            return Error(Gs1LintError.ZeroTotalPieces, half, half);

        if (piece.SequenceCompareTo(total) > 0)
            return Error(Gs1LintError.PieceNumberExceedsTotal, 0, data.Length);

        return Gs1LintResult.Ok;
    }

    // "<pos>/<end>", such as "1/2"
    private static Gs1LintResult PositionInSequence(ReadOnlySpan<char> data)
    {
        var slash = 0;
        while (slash < data.Length && IsDigit(data[slash]))
        {
            slash++;
        }

        if (slash == 0 || slash >= data.Length - 1 || data[slash] != '/' || data[(slash + 1)..].ContainsAnyExceptInRange('0', '9'))
            return Error(Gs1LintError.PositionInSequenceMalformed, 0, data.Length);

        var position = data[..slash];
        var end = data[(slash + 1)..];
        if (position[0] == '0')
            return Error(Gs1LintError.IllegalZeroPrefix, 0, slash);

        if (end[0] == '0')
            return Error(Gs1LintError.IllegalZeroPrefix, slash + 1, end.Length);

        if (position.Length > end.Length || (position.Length == end.Length && position.SequenceCompareTo(end) > 0))
            return Error(Gs1LintError.PositionExceedsEnd, 0, data.Length);

        return Gs1LintResult.Ok;
    }

    private static Gs1LintResult Winding(ReadOnlySpan<char> data)
    {
        if (data.Length != 1)
            return Error(Gs1LintError.InvalidWindingDirection, 0, data.Length);

        if (data[0] is not ('0' or '1' or '9'))
            return Error(Gs1LintError.InvalidWindingDirection, 0, 1);

        return Gs1LintResult.Ok;
    }

    private static Gs1LintResult YesNo(ReadOnlySpan<char> data)
    {
        if (data.Length != 1)
            return Error(Gs1LintError.NotZeroOrOne, 0, data.Length);

        if (data[0] is not ('0' or '1'))
            return Error(Gs1LintError.NotZeroOrOne, 0, 1);

        return Gs1LintResult.Ok;
    }

    // YYMMDD where DD may be 00 (the last day of the month)
    private static Gs1LintResult Yymmd0(ReadOnlySpan<char> data)
    {
        if (data.Length != 6)
            return Error(data.Length < 6 ? Gs1LintError.DateTooShort : Gs1LintError.DateTooLong, 0, data.Length);

        var digits = CheckDigits(data);
        if (!digits.IsValid)
            return digits;

        // GS1 General Specifications, section 7.12: the year is in the 100 years around the current year
        var yy = (Digit(data[0]) * 10) + Digit(data[1]);
        var currentCentury = CurrentYear / 100 * 100;
        var difference = yy - (CurrentYear % 100);
        var year = difference >= 51 ? currentCentury - 100 + yy : difference > -50 ? currentCentury + yy : currentCentury + 100 + yy;

        Span<char> yyyymmdd = stackalloc char[8];
        year.TryFormat(yyyymmdd, out _, "0000", CultureInfo.InvariantCulture);
        data[2..].CopyTo(yyyymmdd[4..]);
        var result = Yyyymmd0(yyyymmdd);
        if (!result.IsValid)
            return result with { Position = result.Position - 2 };

        return Gs1LintResult.Ok;
    }

    private static Gs1LintResult Yymmdd(ReadOnlySpan<char> data)
    {
        var result = Yymmd0(data);
        if (!result.IsValid)
            return result;

        if (data[4] == '0' && data[5] == '0')
            return Error(Gs1LintError.IllegalDay, 4, 2);

        return Gs1LintResult.Ok;
    }

    private static Gs1LintResult Yyyymmd0(ReadOnlySpan<char> data)
    {
        if (data.Length != 8)
            return Error(data.Length < 8 ? Gs1LintError.DateTooShort : Gs1LintError.DateTooLong, 0, data.Length);

        var digits = CheckDigits(data);
        if (!digits.IsValid)
            return digits;

        var year = (Digit(data[0]) * 1000) + (Digit(data[1]) * 100) + (Digit(data[2]) * 10) + Digit(data[3]);
        var month = (Digit(data[4]) * 10) + Digit(data[5]);
        var day = (Digit(data[6]) * 10) + Digit(data[7]);
        if (month is < 1 or > 12)
            return Error(Gs1LintError.IllegalMonth, 4, 2);

        int maxDay = month switch
        {
            2 => (year % 4 == 0 && year % 100 != 0) || year % 400 == 0 ? 29 : 28,
            4 or 6 or 9 or 11 => 30,
            _ => 31,
        };

        // A day of 00 is permitted
        if (day > maxDay)
            return Error(Gs1LintError.IllegalDay, 6, 2);

        return Gs1LintResult.Ok;
    }

    private static Gs1LintResult Yyyymmdd(ReadOnlySpan<char> data)
    {
        var result = Yyyymmd0(data);
        if (!result.IsValid)
            return result;

        if (data[6] == '0' && data[7] == '0')
            return Error(Gs1LintError.IllegalDay, 6, 2);

        return Gs1LintResult.Ok;
    }

    private static Gs1LintResult Zero(ReadOnlySpan<char> data)
    {
        if (data.IsEmpty)
            return Error(Gs1LintError.NotZero, 0, 0);

        if (data.ContainsAnyExcept('0'))
            return Error(Gs1LintError.NotZero, 0, data.Length);

        return Gs1LintResult.Ok;
    }

    public static string GetMessage(Gs1LintError error)
    {
        return error switch
        {
            Gs1LintError.None => "No issues were detected by the linter.",
            Gs1LintError.NonDigitCharacter => "A non-digit character was found where a digit is expected.",
            Gs1LintError.InvalidCset82Character => "A non-CSET 82 character was found where a CSET 82 character is expected.",
            Gs1LintError.InvalidCset39Character => "A non-CSET 39 character was found where a CSET 39 character is expected.",
            Gs1LintError.InvalidCset32Character => "A non-CSET 32 character was found where a CSET 32 character is expected.",
            Gs1LintError.IncorrectCheckDigit => "The numeric check digit is incorrect.",
            Gs1LintError.TooShortForCheckDigit => "The component is too short to perform a numeric check digit calculation.",
            Gs1LintError.IncorrectCheckPair => "The alphanumeric check-character pair are incorrect.",
            Gs1LintError.TooShortForCheckPair => "The component is too short to perform an alphanumeric check character pair calculation.",
            Gs1LintError.TooLongForCheckPairImplementation => "The component is too long to perform an alphanumeric check character pair calculation.",
            Gs1LintError.InvalidGcpPrefix => "The GS1 Company Prefix is invalid.",
            Gs1LintError.ImporterIdxMustBeOneCharacter => "The Importer Index must be a single character.",
            Gs1LintError.InvalidImportIdxCharacter => "The Importer Index is an invalid character.",
            Gs1LintError.IllegalZeroValue => "A non-zero value is required.",
            Gs1LintError.NotZero => "A zero is required.",
            Gs1LintError.IllegalZeroPrefix => "A zero prefix is not permitted.",
            Gs1LintError.NotZeroOrOne => "A \"0\" or \"1\" is required.",
            Gs1LintError.InvalidWindingDirection => "The winding direction must be either \"0\", \"1\" or \"9\".",
            Gs1LintError.NotIso3166 => "A valid ISO 3166 three-digit country code is required.",
            Gs1LintError.NotIso3166Or999 => "A valid ISO 3166 three-digit country code or \"999\" is required.",
            Gs1LintError.NotIso3166Alpha2 => "A valid ISO 3166 two-character country code is required.",
            Gs1LintError.NotIso4217 => "A valid ISO 4217 three-digit currency code is required.",
            Gs1LintError.IbanTooShort => "The IBAN is too short.",
            Gs1LintError.InvalidIbanCharacter => "The IBAN contains an invalid character.",
            Gs1LintError.IllegalIbanCountryCode => "The IBAN must start with a valid ISO 3166 two-character country code.",
            Gs1LintError.IncorrectIbanChecksum => "The IBAN is invalid since the check characters are incorrect.",
            Gs1LintError.DateTooShort => "The date is too short.",
            Gs1LintError.DateTooLong => "The date is too long.",
            Gs1LintError.HourWithMinuteTooShort => "The hour with minute is too short for HHMI format.",
            Gs1LintError.HourWithMinuteTooLong => "The hour with minute is too long for HHMI format.",
            Gs1LintError.IllegalMonth => "The date contains an illegal month of the year.",
            Gs1LintError.IllegalDay => "The date contains an illegal day of the month.",
            Gs1LintError.IllegalHour => "The time contains an illegal hour.",
            Gs1LintError.IllegalMinute => "The time contains an illegal minute.",
            Gs1LintError.IllegalSecond => "The time contains an illegal seconds.",
            Gs1LintError.InvalidLengthForPieceOfTotal => "The piece with total must have an even length, having equal-length components.",
            Gs1LintError.ZeroPieceNumber => "The piece number must not have a value of zero.",
            Gs1LintError.ZeroTotalPieces => "The piece total must not have a value of zero.",
            Gs1LintError.PieceNumberExceedsTotal => "The piece number must not exceed the piece total.",
            Gs1LintError.InvalidPercentSequence => "The input contains an invalid percent hex-encoding \"%hh\" sequence.",
            Gs1LintError.CouponMissingFormatCode => "The coupon's Format Code is missing.",
            Gs1LintError.CouponInvalidFormatCode => "The coupon's Format Code must be \"0\" or \"1\".",
            Gs1LintError.CouponMissingFunderVli => "The coupon's Funder VLI is missing.",
            Gs1LintError.CouponInvalidFunderLength => "The coupon's Funder VLI must be \"0\" to \"6\".",
            Gs1LintError.CouponTruncatedFunder => "The coupon's Funder is shorter than what is indicated by its VLI.",
            Gs1LintError.CouponTruncatedOfferCode => "The coupon's Offer Code is shorter than the required six digits.",
            Gs1LintError.CouponMissingSerialNumberVli => "The coupon's Serial Number VLI is missing.",
            Gs1LintError.CouponTruncatedSerialNumber => "The coupon's Serial Number is shorter than what is indicated by its VLI.",
            Gs1LintError.CouponMissingGcpVli => "The coupon's primary GS1 Company Prefix VLI is missing.",
            Gs1LintError.CouponInvalidGcpLength => "The coupon's primary GS1 Company Prefix VLI must be \"0\" to \"6\".",
            Gs1LintError.CouponTruncatedGcp => "The coupon's primary GS1 Company Prefix is shorter than what is indicated by its VLI.",
            Gs1LintError.CouponMissingSaveValueVli => "The coupon's Save Value VLI is missing.",
            Gs1LintError.CouponInvalidSaveValueLength => "The coupon's Save Value VLI must be \"1\" to \"5\".",
            Gs1LintError.CouponTruncatedSaveValue => "The coupon's Save Value is shorter than what is indicated by its VLI.",
            Gs1LintError.CouponMissing1stPurchaseRequirementVli => "The coupon's primary purchase Requirement VLI is missing.",
            Gs1LintError.CouponInvalid1stPurchaseRequirementLength => "The coupon's primary purchase Requirement VLI must be \"1\" to \"5\".",
            Gs1LintError.CouponTruncated1stPurchaseRequirement => "The coupon's primary purchase Requirement is shorter than what is indicated by its VLI.",
            Gs1LintError.CouponMissing1stPurchaseRequirementCode => "The coupon's primary purchase Requirement Code is missing.",
            Gs1LintError.CouponInvalid1stPurchaseRequirementCode => "The coupon's primary purchase Requirement Code must be \"0\" to \"4\" or \"9\".",
            Gs1LintError.CouponTruncated1stPurchaseFamilyCode => "The coupon's primary purchase Family Code is shorter than the required three digits.",
            Gs1LintError.CouponMissingAdditionalPurchaseRulesCode => "The coupon's Additional Purchase Rules Code is missing.",
            Gs1LintError.CouponInvalidAdditionalPurchaseRulesCode => "The coupon's Additional Purchase Rules Code must be \"0\" to \"3\".",
            Gs1LintError.CouponMissing2ndPurchaseRequirementVli => "The coupon's second purchase Requirement VLI is missing.",
            Gs1LintError.CouponInvalid2ndPurchaseRequirementLength => "The coupon's second purchase Requirement VLI must be \"1\" to \"5\".",
            Gs1LintError.CouponTruncated2ndPurchaseRequirement => "The coupon's second purchase Requirement is shorter than what is indicated by its VLI.",
            Gs1LintError.CouponMissing2ndPurchaseRequirementCode => "The coupon's second purchase Requirement Code is missing.",
            Gs1LintError.CouponInvalid2ndPurchaseRequirementCode => "The coupon's second purchase Requirement Code must be \"0\" to \"4\" or \"9\".",
            Gs1LintError.CouponTruncated2ndPurchaseFamilyCode => "The coupon's second purchase Family Code is shorter than the required three digits.",
            Gs1LintError.CouponMissing2ndPurchaseGcpVli => "The coupon's second purchase GS1 Company Prefix VLI is missing.",
            Gs1LintError.CouponInvalid2ndPurchaseGcpLength => "The coupon's second purchase GS1 Company Prefix VLI must be \"0\" to \"6\" or \"9\".",
            Gs1LintError.CouponTruncated2ndPurchaseGcp => "The coupon's second purchase GS1 Company Prefix is shorter than what is indicated by its VLI.",
            Gs1LintError.CouponMissing3rdPurchaseRequirementVli => "The coupon's third purchase Requirement VLI is missing.",
            Gs1LintError.CouponInvalid3rdPurchaseRequirementLength => "The coupon's third purchase Requirement VLI must be \"1\" to \"5\".",
            Gs1LintError.CouponTruncated3rdPurchaseRequirement => "The coupon's third purchase Requirement is shorter than what is indicated by its VLI.",
            Gs1LintError.CouponMissing3rdPurchaseRequirementCode => "The coupon's third purchase Requirement Code is missing.",
            Gs1LintError.CouponInvalid3rdPurchaseRequirementCode => "The coupon's third purchase Requirement Code must be \"0\" to \"4\" or \"9\".",
            Gs1LintError.CouponTruncated3rdPurchaseFamilyCode => "The coupon's third purchase Family Code is shorter than the required three digits.",
            Gs1LintError.CouponMissing3rdPurchaseGcpVli => "The coupon's third purchase GS1 Company Prefix VLI is missing.",
            Gs1LintError.CouponInvalid3rdPurchaseGcpLength => "The coupon's third purchase GS1 Company Prefix VLI must be \"0\" to \"6\" or \"9\".",
            Gs1LintError.CouponTruncated3rdPurchaseGcp => "The coupon's third purchase GS1 Company Prefix is shorter than what is indicated by its VLI.",
            Gs1LintError.CouponTooShortForExpirationDate => "The coupon's expiration date is too short for YYMMDD format.",
            Gs1LintError.CouponInvalidExpirationDate => "The coupon's expiration date is invalid.",
            Gs1LintError.CouponTooShortForStartDate => "The coupon's start date is too short to YYMMDD format.",
            Gs1LintError.CouponInvalidStartDate => "The coupon's start date is invalid.",
            Gs1LintError.CouponExpirationBeforeStart => "The coupon's expiration date precede the start date.",
            Gs1LintError.CouponMissingRetailerGcpOrGlnVli => "The coupon's Retailer GCP/GLN VLI is missing.",
            Gs1LintError.CouponInvalidRetailerGcpOrGlnLength => "The coupon's Retailer GCP/GLN VLI must be \"1\" to \"7\".",
            Gs1LintError.CouponTruncatedRetailerGcpOrGln => "The coupon's Retailer GCP/GLN is shorter than what is indicated by its VLI.",
            Gs1LintError.CouponMissingSaveValueCode => "The coupon's Save Value Code is missing.",
            Gs1LintError.CouponInvalidSaveValueCode => "The coupon's Save Value Code must be \"0\", \"1\", \"2\", \"5\" or \"6\".",
            Gs1LintError.CouponMissingSaveValueAppliesToItem => "The coupon's Save Value Applies to Item is missing.",
            Gs1LintError.CouponInvalidSaveValueAppliesToItem => "The coupon's Save Value Applies to Item must be \"0\" to \"2\".",
            Gs1LintError.CouponMissingStoreCouponFlag => "The coupon's Store Coupon Flag is missing.",
            Gs1LintError.CouponMissingDontMultiplyFlag => "The coupon's Don't Multiply Flag is missing.",
            Gs1LintError.CouponInvalidDontMultiplyFlag => "The coupon's Don't Multiply Flag must be \"0\" or \"1\".",
            Gs1LintError.CouponExcessData => "The coupon contains excess data after the recognised optional fields.",
            Gs1LintError.InvalidLatitude => "The latitude is outside of the range \"0000000000\" to \"1800000000\".",
            Gs1LintError.InvalidLongitude => "The longitude is outside of the range \"0000000000\" to \"3600000000\".",
            Gs1LintError.InvalidMediaType => "A valid AIDC media type is required.",
            Gs1LintError.LatitudeInvalidLength => "The latitude must be 10 digits.",
            Gs1LintError.LongitudeInvalidLength => "The longitude must be 10 digits.",
            Gs1LintError.InvalidCset64Character => "A non-CSET 64 character was found where a CSET 64 character is expected.",
            Gs1LintError.InvalidCset64Padding => "Incorrect number of CSET 64 pad characters.",
            Gs1LintError.NotHyphen => "Only hyphens are permitted.",
            Gs1LintError.InvalidBiologicalSexCode => "A valid ISO/IEC 5218 biological sex code required.",
            Gs1LintError.PositionInSequenceMalformed => "The data must have the format \"<pos>/<end>\".",
            Gs1LintError.PositionExceedsEnd => "The position number must not exceed the end number.",
            Gs1LintError.RequiresNonDigitCharacter => "A non-digit character is required.",
            Gs1LintError.HourTooShort => "The hour is too short for HH format.",
            Gs1LintError.HourTooLong => "The hour is too long for HH format.",
            Gs1LintError.MinuteTooShort => "The minute is too short for MI format.",
            Gs1LintError.MinuteTooLong => "The minute is too long for MI format.",
            Gs1LintError.SecondTooShort => "The second is too short for SS format.",
            Gs1LintError.SecondTooLong => "The second is too long for SS format.",
            Gs1LintError.InvalidPackageType => "A valid PackageTypeCode is required.",
            Gs1LintError.TooShortForGcp => "The component is shorter than the minimum length GS1 Company Prefix.",
            Gs1LintError.IbanTooLong => "The IBAN is too long.",
            _ => error.ToString(),
        };
    }
}
