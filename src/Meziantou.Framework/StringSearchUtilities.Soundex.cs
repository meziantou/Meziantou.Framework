using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Text;
#if NET461 || NETSTANDARD2_0
using System.Linq;
#elif NETCOREAPP3_1
#else
#error Platform not supported
#endif

namespace Meziantou.Framework
{
    public static partial class StringSearchUtilities
    {
        /// <summary>
        /// Compute the soundex of a string.
        /// </summary>
        /// <param name="s"> The string.</param>
        /// <param name="dic"> Dictionary containing value of characters.</param>
        /// <param name="replace"> List of replacement to do before computing the soundex.</param>
        /// <returns> The soundex.</returns>
        /// <exception cref="ArgumentException">Dictionary does not contain character a character of the string <paramref name="s" /></exception>
        [Pure]
        public static string Soundex(string s, IReadOnlyDictionary<char, byte> dic, IReadOnlyDictionary<string, char>? replace = null)
        {
            if (s == null)
                throw new ArgumentNullException(nameof(s));
            if (dic == null)
                throw new ArgumentNullException(nameof(dic));

            s = SoundexStringPrep(s, replace);
            if (s.Length == 0)
            {
                return "0000";
            }

            var sb = s[0].ToString(CultureInfo.InvariantCulture);

            var oldPos = true;
            if (!dic.TryGetValue(s[0], out var oldCode))
            {
                // throw new ArgumentException(string.Format("Dictionary does not contain character '{0}'", s[0]), "dic");
                oldCode = 0;
            }

            var i = 1;
            while (sb.Length < 4 && i < s.Length)
            {
                if (dic.TryGetValue(s[i], out var value))
                {
                    if (value > 0)
                    {
                        if (value != oldCode)
                        {
                            sb += value;
                            oldPos = true;
                        }
                        else
                        {
                            if (!oldPos)
                            {
                                sb += value;
                                oldPos = true;
                            }
                        }
                    }
                }
                else
                {
                    oldPos = oldPos && (s[i] == 'H' || s[i] == 'W');
                }

                if (value > 0)
                {
                    oldCode = value;
                }

                i++;
            }

            // Soundex must be four character long
            while (sb.Length < 4)
            {
                sb += '0';
            }

            return sb;
        }

        /// <summary>
        ///     Compute an improved French soundex.
        /// </summary>
        /// <param name="s"> The string. </param>
        /// <returns> The soundex. </returns>
        [Pure]
        public static string Soundex2(string s)
        {
            if (s == null)
                throw new ArgumentNullException(nameof(s));

            var sb = new StringBuilder();
            foreach (var t in s.TrimStart().ToUpperInvariant().RemoveDiacritics())
            {
                if (t != ' ')
                {
                    sb.Append(t);
                }
                else
                {
                    break;
                }
            }

            if (sb.Length == 0)
            {
                return "    ";
            }

            if (sb.Length == 1)
            {
                sb.Append("   ");
                return sb.ToString();
            }

            sb = sb.Replace("GUI", "KI");
            sb = sb.Replace("GUE", "KE");
            sb = sb.Replace("GA", "KA");
            sb = sb.Replace("GO", "KO");
            sb = sb.Replace("GU", "K");
            sb = sb.Replace("CA", "KA");
            sb = sb.Replace("CO", "KO");
            sb = sb.Replace("CU", "KU");
            sb = sb.Replace("Q", "K");
            sb = sb.Replace("CC", "K");
            sb = sb.Replace("CK", "K");

            // Replace E, I, O, U by A
            for (var i = 1; i < sb.Length; i++)
            {
                switch (sb[i])
                {
                    case 'E':
                    case 'I':
                    case 'O':
                    case 'U':
                        sb[i] = 'A';
                        break;
                }
            }

            ChangePrefix(sb, "MAC", "MCC");
            ChangePrefix(sb, "ASA", "AZA");
            ChangePrefix(sb, "KN", "NN");
            ChangePrefix(sb, "PF", "FF");
            ChangePrefix(sb, "SCH", "SSS");
            ChangePrefix(sb, "PH", "FF");

            // Remove H except if the previous letter is a C or an S
            var cs = false;
            for (var i = 0; i < sb.Length; i++)
            {
                if (sb[i] == 'H' && !cs)
                {
                    sb = sb.Remove(i, 1);
                    i = i == 0 ? 0 : i - 1;
                }

                if (sb.Length > i)
                {
                    cs = "CS".Contains(sb[i]);
                }
            }

            // Remove Y except if the previous letter is an A
            var a = false;
            for (var i = 0; i < sb.Length; i++)
            {
                if (sb[i] == 'Y' && !a)
                {
                    sb = sb.Remove(i, 1);
                    i = i == 0 ? 0 : i - 1;
                }

                if (sb.Length > i)
                {
                    a = sb[i] == 'A';
                }
            }

            // Remove the last character if it's an A or a T or a D or an S
            if (sb.Length > 0 && "ATDS".Contains(sb[sb.Length - 1]))
            {
                sb = sb.Remove(sb.Length - 1, 1);
            }

            // Remove all A except if the A is the first letter
            for (var i = 1; i < sb.Length; i++)
            {
                if (sb[i] == 'A')
                {
                    sb = sb.Remove(i, 1);
                    i--;
                }
            }

            // Remove substring composed of repeated letters
            for (var i = 0; i < sb.Length - 1; i++)
            {
                if (sb[i] == sb[i + 1])
                {
                    sb = sb.Remove(i, 1);
                    i--;
                }
            }

            // The soundex must be four character long.
            while (sb.Length < 4)
            {
                sb.Append(' ');
            }

            return sb.ToString(0, 4);
        }

        /// <summary>
        ///     Compute English soundex.
        /// </summary>
        /// <param name="s"> The string. </param>
        /// <returns> The soundex. </returns>
        [Pure]
        public static string SoundexEnglish(string s)
        {
            var dic = new Dictionary<char, byte>
                {
                    {'B', 1},
                    {'F', 1},
                    {'P', 1},
                    {'V', 1},
                    {'C', 2},
                    {'G', 2},
                    {'J', 2},
                    {'K', 2},
                    {'Q', 2},
                    {'S', 2},
                    {'X', 2},
                    {'Z', 2},
                    {'D', 3},
                    {'T', 3},
                    {'L', 4},
                    {'M', 5},
                    {'N', 5},
                    {'R', 6},
                };

            return Soundex(s, dic);
        }

        /// <summary>
        ///     Compute French soundex.
        /// </summary>
        /// <param name="s"> The string. </param>
        /// <returns> The soundex. </returns>
        [Pure]
        public static string SoundexFrench(string s)
        {
            var dic = new Dictionary<char, byte>
                {
                    {'B', 1},
                    {'P', 1},
                    {'C', 2},
                    {'K', 2},
                    {'Q', 2},
                    {'D', 3},
                    {'T', 3},
                    {'L', 4},
                    {'M', 5},
                    {'N', 5},
                    {'R', 6},
                    {'G', 7},
                    {'J', 7},
                    {'S', 8},
                    {'X', 8},
                    {'Z', 8},
                    {'F', 9},
                    {'V', 9},
                };

            return Soundex(s, dic);
        }

        private static void ChangePrefix(StringBuilder sb, string prefix, string replace)
        {
            if (sb == null)
                throw new ArgumentNullException(nameof(sb));
            if (prefix == null)
                throw new ArgumentNullException(nameof(prefix));
            if (replace == null)
                throw new ArgumentNullException(nameof(replace));

            var i = 0;
            while (i < sb.Length && i < prefix.Length)
            {
                if (sb[i] != prefix[i])
                    return;

                i++;
            }

            if (!sb.StartsWith(prefix)) // TODO remove?
                return;

            sb.Replace(prefix, replace, 0, 1);
        }

        [Pure]
        private static string SoundexStringPrep(string s, IReadOnlyDictionary<string, char>? replace = null)
        {
            if (s == null)
                throw new ArgumentNullException(nameof(s));

            // takes only the first word of the string.
            var sb = new StringBuilder();
            foreach (var t in s.TrimStart().ToUpperInvariant().RemoveDiacritics())
            {
                if (char.IsWhiteSpace(t))
                    break; // Exit after the first space

                if (char.IsLetter(t))
                {
                    sb.Append(t);
                }
            }

            // Return empty string if string is empty
            if (sb.Length == 0)
                return string.Empty;

            // Replace characters
            if (replace != null)
            {
                foreach (var pair in replace)
                {
                    sb = sb.Replace(pair.Key, pair.Value.ToString(CultureInfo.InvariantCulture));
                }
            }

            return sb.ToString();
        }
    }
}
