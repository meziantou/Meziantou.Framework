using System;
using System.Diagnostics.Contracts;
using System.Text;

namespace Meziantou.Framework
{
    public static partial class StringSearchUtilities
    {
        [Pure]
        public static string Metaphone(string s)
        {
            if (s == null)
                throw new ArgumentNullException(nameof(s));

            const string Vowels = "AEIOU";
            const string Frontv = "EIY";
            const string Varson = "CSPTG";
            const int MaxCodeLen = 4;

            if (s.Length == 0)
                return string.Empty;

            if (s.Length == 1)
                return s.ToUpperInvariant();

            var inwd = s.ToUpperInvariant().ToCharArray();
            var local = new StringBuilder(40); // manipulate
            var code = new StringBuilder(10); // output

            // handle initial 2 characters exceptions
            switch (inwd[0])
            {
                case 'K':
                case 'G':
                case 'P': /* looking for KN, etc*/
                    if (inwd[1] == 'N')
                    {
                        local.Append(inwd, 1, inwd.Length - 1);
                    }
                    else
                    {
                        local.Append(inwd);
                    }

                    break;
                case 'A': /* looking for AE */
                    if (inwd[1] == 'E')
                    {
                        local.Append(inwd, 1, inwd.Length - 1);
                    }
                    else
                    {
                        local.Append(inwd);
                    }

                    break;
                case 'W': /* looking for WR or WH */
                    if (inwd[1] == 'R')
                    {
                        // WR -> R
                        local.Append(inwd, 1, inwd.Length - 1);
                        break;
                    }

                    if (inwd[1] == 'H')
                    {
                        local.Append(inwd, 1, inwd.Length - 1);
                        local[0] = 'W'; // WH -> W
                    }
                    else
                    {
                        local.Append(inwd);
                    }

                    break;
                case 'X': /* initial X becomes S */
                    inwd[0] = 'S';
                    local.Append(inwd);
                    break;
                default:
                    local.Append(inwd);
                    break;
            }

            // now local has working string with initials fixed
            var wdsz = local.Length;
            var mtsz = 0;
            var n = 0;
            while ((mtsz < MaxCodeLen) && // max code size of 4 works well
                   (n < wdsz))
            {
                var symb = local[n];

                // remove duplicate letters except C
                if ((symb != 'C') && (n > 0) && (local[n - 1] == symb))
                {
                    n++;
                }
                else
                {
                    // not dup
                    string tmpS;
                    switch (symb)
                    {
                        case 'A':
                        case 'E':
                        case 'I':
                        case 'O':
                        case 'U':
                            if (n == 0)
                            {
                                code.Append(symb);
                                mtsz++;
                            }

                            break; // only use vowel if leading char
                        case 'B':
                            if (((n > 0) && n == wdsz - 1) && (local[n - 1] == 'M'))
                            {
                                break;
                            }

                            code.Append(symb);
                            mtsz++;
                            break;

                        case 'C': // lots of C special cases
                            /* discard if SCI, SCE or SCY */
                            if ((n > 0) && (local[n - 1] == 'S') && (n + 1 < wdsz)
                                && (Frontv.IndexOf(local[n + 1]) >= 0))
                            {
                                break;
                            }

                            tmpS = local.ToString();
                            Contract.Assume(local.Length == tmpS.Length);
                            if (tmpS.IndexOf("CIA", n, StringComparison.Ordinal) == n)
                            {
                                // "CIA" -> X
                                code.Append('X');
                                mtsz++;
                                break;
                            }

                            if ((n + 1 < wdsz) && (Frontv.IndexOf(local[n + 1]) >= 0))
                            {
                                code.Append('S');
                                mtsz++;
                                break; // CI,CE,CY -> S
                            }

                            if ((n > 0) && (tmpS.IndexOf("SCH", n - 1, StringComparison.Ordinal) == n - 1))
                            {
                                // SCH->sk
                                code.Append('K');
                                mtsz++;
                                break;
                            }

                            if (tmpS.IndexOf("CH", n, StringComparison.Ordinal) == n)
                            {
                                // detect CH
                                if ((n == 0) && (wdsz >= 3) && // CH consonant -> K consonant
                                    (Vowels.IndexOf(local[2]) < 0))
                                {
                                    code.Append('K');
                                }
                                else
                                {
                                    code.Append('X'); // CHvowel -> X
                                }

                                mtsz++;
                            }
                            else
                            {
                                code.Append('K');
                                mtsz++;
                            }

                            break;

                        case 'D':
                            if ((n + 2 < wdsz) && // DGE DGI DGY -> J
                                (local[n + 1] == 'G') && (Frontv.IndexOf(local[n + 2]) >= 0))
                            {
                                code.Append('J');
                                n += 2;
                            }
                            else
                            {
                                code.Append('T');
                            }

                            mtsz++;
                            break;

                        case 'G': // GH silent at end or before consonant
                            if ((n + 2 == wdsz) && (local[n + 1] == 'H'))
                                break;

                            if ((n + 2 < wdsz) && (local[n + 1] == 'H') && (Vowels.IndexOf(local[n + 2]) < 0))
                                break;

                            tmpS = local.ToString();
                            if (n > 0 && (tmpS.IndexOf("GN", n, StringComparison.Ordinal) == n || tmpS.IndexOf("GNED", n, StringComparison.Ordinal) == n))
                                break; // silent G

                            // bool hard = false;
                            // if ((n > 0) &&
                            // (local[n - 1] == 'G')) hard = true;//totest
                            // else hard = false;
                            if ((n + 1 < wdsz) && (Frontv.IndexOf(local[n + 1]) >= 0) /*&& !hard*/)
                            {
                                code.Append('J');
                            }
                            else
                            {
                                code.Append('K');
                            }

                            mtsz++;
                            break;

                        case 'H':
                            if (n + 1 == wdsz)
                                break; // terminal H

                            if (n > 0 && Varson.IndexOf(local[n - 1]) >= 0)
                                break;

                            if (Vowels.IndexOf(local[n + 1]) >= 0)
                            {
                                code.Append('H');
                                mtsz++; // Hvowel
                            }

                            break;

                        case 'F':
                        case 'J':
                        case 'L':
                        case 'M':
                        case 'N':
                        case 'R':
                            code.Append(symb);
                            mtsz++;
                            break;

                        case 'K':
                            if (n > 0)
                            {
                                // not initial
                                if (local[n - 1] != 'C')
                                {
                                    code.Append(symb);
                                }
                            }
                            else
                            {
                                code.Append(symb); // initial K
                            }

                            mtsz++;
                            break;

                        case 'P':
                            // PH -> F
                            if ((n + 1 < wdsz) && (local[n + 1] == 'H'))
                            {
                                code.Append('F');
                            }
                            else
                            {
                                code.Append(symb);
                            }

                            mtsz++;
                            break;

                        case 'Q':
                            code.Append('K');
                            mtsz++;
                            break;

                        case 'S':
                            tmpS = local.ToString();
                            Contract.Assume(tmpS.Length == local.Length);
                            if ((tmpS.IndexOf("SH", n, StringComparison.Ordinal) == n) || (tmpS.IndexOf("SIO", n, StringComparison.Ordinal) == n)
                                || (tmpS.IndexOf("SIA", n, StringComparison.Ordinal) == n))
                            {
                                code.Append('X');
                            }
                            else
                            {
                                code.Append('S');
                            }

                            mtsz++;
                            break;

                        case 'T':
                            tmpS = local.ToString(); // TIA TIO -> X
                            Contract.Assume(tmpS.Length == local.Length);
                            if ((tmpS.IndexOf("TIA", n, StringComparison.Ordinal) == n) || (tmpS.IndexOf("TIO", n, StringComparison.Ordinal) == n))
                            {
                                code.Append('X');
                                mtsz++;
                                break;
                            }

                            if (tmpS.IndexOf("TCH", n, StringComparison.Ordinal) == n)
                                break;

                            // substitute numeral 0 for TH (resembles theta after all)
                            code.Append(tmpS.IndexOf("TH", n, StringComparison.Ordinal) == n ? '0' : 'T');
                            mtsz++;
                            break;

                        case 'V':
                            code.Append('F');
                            mtsz++;
                            break;

                        case 'W':
                        case 'Y': // silent if not followed by vowel
                            if ((n + 1 < wdsz) && (Vowels.IndexOf(local[n + 1]) >= 0))
                            {
                                code.Append(symb);
                                mtsz++;
                            }

                            break;

                        case 'X':
                            code.Append('K');
                            code.Append('S');
                            mtsz += 2;
                            break;

                        case 'Z':
                            code.Append('S');
                            mtsz++;
                            break;
                    }

                    // end switch
                    n++;
                }

                // end else from symb != 'C'
                if (mtsz > 4)
                {
                    code.Length = 4;
                }
            }

            return code.ToString();
        }
    }
}
