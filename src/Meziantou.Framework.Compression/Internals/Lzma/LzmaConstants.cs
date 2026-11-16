namespace Meziantou.Framework.Compression.Internals.Lzma;

/// <summary>The constants of the LZMA format (LzmaDec.c and LzmaEnc.c of the LZMA SDK, which is in the public domain).</summary>
internal static class LzmaConstants
{
    public const int NumStates = 12;
    public const int NumPosBitsMax = 4;
    public const int NumPosStatesMax = 1 << NumPosBitsMax;
    public const int NumLenToPosStates = 4;
    public const int NumPosSlotBits = 6;
    public const int StartPosModelIndex = 4;
    public const int EndPosModelIndex = 14;
    public const int NumFullDistances = 1 << (EndPosModelIndex >> 1);
    public const int NumAlignBits = 4;
    public const int AlignTableSize = 1 << NumAlignBits;
    public const int AlignMask = AlignTableSize - 1;
    public const int LenNumLowBits = 3;
    public const int LenNumMidBits = 3;
    public const int LenNumHighBits = 8;
    public const int LenNumLowSymbols = 1 << LenNumLowBits;
    public const int LenNumMidSymbols = 1 << LenNumMidBits;
    public const int LenNumHighSymbols = 1 << LenNumHighBits;
    public const int LenNumSymbolsTotal = LenNumLowSymbols + LenNumMidSymbols + LenNumHighSymbols;
    public const int NumLenProbs = 2 + (NumPosStatesMax << LenNumLowBits) + (NumPosStatesMax << LenNumMidBits) + LenNumHighSymbols;
    public const int MatchLenMin = 2;
    public const int MatchLenMax = MatchLenMin + LenNumSymbolsTotal - 1;
    public const int NumReps = 4;
    public const int MinDictionarySize = 1 << 12;

    /// <summary>Gets the dictionary size that an LZMA2 dictionary property (0 to 40) encodes.</summary>
    public static uint GetLzma2DictionarySize(byte property)
    {
        if (property > 40)
            throw new InvalidDataException("The LZMA2 dictionary size is invalid.");

        if (property == 40)
            return uint.MaxValue;

        return (2u | (property & 1u)) << ((property / 2) + 11);
    }

    /// <summary>Gets the smallest LZMA2 dictionary property whose dictionary size is at least <paramref name="dictionarySize"/>.</summary>
    public static byte GetLzma2DictionaryProperty(uint dictionarySize)
    {
        for (byte property = 0; property < 40; property++)
        {
            if (GetLzma2DictionarySize(property) >= dictionarySize)
                return property;
        }

        return 40;
    }

    /// <summary>Encodes the lc/lp/pb byte of the LZMA properties.</summary>
    public static byte EncodeProperties(int lc, int lp, int pb) => (byte)((((pb * 5) + lp) * 9) + lc);
}
