using System.Buffers;

namespace Meziantou.Framework;

public static partial class StringSearchUtilities
{
    public static string Metaphone(string s)
    {
        ArgumentNullException.ThrowIfNull(s);

        if (s.Length is 0)
            return "";

        if (s.Length is 1)
            return s.ToUpperInvariant();

        const int StackAllocThreshold = 256;
        char[]? rentedBuffer = null;
        var buffer = s.Length <= StackAllocThreshold
            ? stackalloc char[StackAllocThreshold]
            : (rentedBuffer = ArrayPool<char>.Shared.Rent(s.Length));

        try
        {
            var upper = buffer[..s.Length];
            s.AsSpan().ToUpperInvariant(upper);
            return MetaphoneCore(upper);
        }
        finally
        {
            if (rentedBuffer is not null)
            {
                ArrayPool<char>.Shared.Return(rentedBuffer);
            }
        }
    }

    private static string MetaphoneCore(Span<char> local)
    {
        const string Vowels = "AEIOU";
        const string Frontv = "EIY";
        const string Varson = "CSPTG";
        const int MaxCodeLen = 4;

        // handle initial 2 characters exceptions
        switch (local[0])
        {
            case 'K' or 'G' or 'P' when local[1] is 'N': // KN, GN, PN -> N
            case 'A' when local[1] is 'E': // AE -> E
            case 'W' when local[1] is 'R': // WR -> R
                local = local[1..];
                break;

            case 'W' when local[1] is 'H': // WH -> W
                local = local[1..];
                local[0] = 'W';
                break;

            case 'X': // initial X becomes S
                local[0] = 'S';
                break;
        }

        // The X case can append 2 letters when the code has MaxCodeLen - 1 letters
        Span<char> code = stackalloc char[MaxCodeLen + 1];
        var codeLength = 0;

        var wdsz = local.Length;
        var n = 0;
        while (codeLength < MaxCodeLen && n < wdsz)
        {
            var symb = local[n];

            // remove duplicate letters except C
            if (symb != 'C' && n > 0 && local[n - 1] == symb)
            {
                n++;
                continue;
            }

            var remaining = local[n..];
            switch (symb)
            {
                case 'A' or 'E' or 'I' or 'O' or 'U':
                    if (n is 0)
                    {
                        code[codeLength++] = symb;
                    }

                    break; // only use vowel if leading char

                case 'B':
                    if (n > 0 && n == wdsz - 1 && local[n - 1] == 'M')
                        break;

                    code[codeLength++] = symb;
                    break;

                case 'C': // lots of C special cases
                    // discard if SCI, SCE or SCY
                    if (n > 0 && local[n - 1] == 'S' && n + 1 < wdsz && Frontv.Contains(local[n + 1], StringComparison.Ordinal))
                        break;

                    if (remaining.StartsWith("CIA"))
                    {
                        code[codeLength++] = 'X'; // CIA -> X
                        break;
                    }

                    if (n + 1 < wdsz && Frontv.Contains(local[n + 1], StringComparison.Ordinal))
                    {
                        code[codeLength++] = 'S'; // CI,CE,CY -> S
                        break;
                    }

                    if (n > 0 && local[(n - 1)..].StartsWith("SCH"))
                    {
                        code[codeLength++] = 'K'; // SCH -> SK
                        break;
                    }

                    if (remaining.StartsWith("CH"))
                    {
                        // CH consonant -> K consonant, CH vowel -> X
                        code[codeLength++] = n is 0 && wdsz >= 3 && !Vowels.Contains(local[2], StringComparison.Ordinal) ? 'K' : 'X';
                    }
                    else
                    {
                        code[codeLength++] = 'K';
                    }

                    break;

                case 'D':
                    if (n + 2 < wdsz && local[n + 1] == 'G' && Frontv.Contains(local[n + 2], StringComparison.Ordinal))
                    {
                        code[codeLength++] = 'J'; // DGE DGI DGY -> J
                        n += 2;
                    }
                    else
                    {
                        code[codeLength++] = 'T';
                    }

                    break;

                case 'G': // GH silent at end or before consonant
                    if (n + 2 == wdsz && local[n + 1] == 'H')
                        break;

                    if (n + 2 < wdsz && local[n + 1] == 'H' && !Vowels.Contains(local[n + 2], StringComparison.Ordinal))
                        break;

                    if (n > 0 && remaining.StartsWith("GN"))
                        break; // silent G

                    code[codeLength++] = n + 1 < wdsz && Frontv.Contains(local[n + 1], StringComparison.Ordinal) ? 'J' : 'K';
                    break;

                case 'H':
                    if (n + 1 == wdsz)
                        break; // terminal H

                    if (n > 0 && Varson.Contains(local[n - 1], StringComparison.Ordinal))
                        break;

                    if (Vowels.Contains(local[n + 1], StringComparison.Ordinal))
                    {
                        code[codeLength++] = 'H'; // H vowel
                    }

                    break;

                case 'F' or 'J' or 'L' or 'M' or 'N' or 'R':
                    code[codeLength++] = symb;
                    break;

                case 'K':
                    // CK -> K
                    if (n is 0 || local[n - 1] != 'C')
                    {
                        code[codeLength++] = symb;
                    }

                    break;

                case 'P':
                    code[codeLength++] = n + 1 < wdsz && local[n + 1] == 'H' ? 'F' : symb; // PH -> F
                    break;

                case 'Q':
                    code[codeLength++] = 'K';
                    break;

                case 'S':
                    code[codeLength++] = remaining.StartsWith("SH") || remaining.StartsWith("SIO") || remaining.StartsWith("SIA") ? 'X' : 'S';
                    break;

                case 'T':
                    if (remaining.StartsWith("TIA") || remaining.StartsWith("TIO"))
                    {
                        code[codeLength++] = 'X'; // TIA TIO -> X
                        break;
                    }

                    if (remaining.StartsWith("TCH"))
                        break;

                    // substitute numeral 0 for TH (resembles theta after all)
                    code[codeLength++] = remaining.StartsWith("TH") ? '0' : 'T';
                    break;

                case 'V':
                    code[codeLength++] = 'F';
                    break;

                case 'W' or 'Y': // silent if not followed by vowel
                    if (n + 1 < wdsz && Vowels.Contains(local[n + 1], StringComparison.Ordinal))
                    {
                        code[codeLength++] = symb;
                    }

                    break;

                case 'X':
                    code[codeLength++] = 'K';
                    code[codeLength++] = 'S';
                    break;

                case 'Z':
                    code[codeLength++] = 'S';
                    break;
            }

            n++;
        }

        return new string(code[..Math.Min(codeLength, MaxCodeLen)]);
    }
}
