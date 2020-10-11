using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Unicode;

namespace Meziantou.Framework
{
    public class SlugOptions
    {
        internal static SlugOptions Default { get; } = new SlugOptions();

        public const int DefaultMaximumLength = 80;
        public const string DefaultSeparator = "-";

        public IList<UnicodeRange> AllowedRanges { get; }

        public int MaximumLength { get; set; }

        public string Separator { get; set; }

        public CultureInfo? Culture { get; set; }

        public bool CanEndWithSeparator { get; set; }

        public CasingTransformation CasingTransformation { get; set; }

        public SlugOptions()
        {
            MaximumLength = DefaultMaximumLength;
            Separator = DefaultSeparator;
            AllowedRanges = new List<UnicodeRange>
            {
                UnicodeRange.Create('a', 'z'),
                UnicodeRange.Create('A', 'Z'),
                UnicodeRange.Create('0', '9'),
            };
        }

        public virtual bool IsAllowed(Rune character)
        {
            return AllowedRanges.Count == 0 || AllowedRanges.Any(range => IsInRange(range, character));
        }

        private static bool IsInRange(UnicodeRange range, Rune rune)
        {
            return rune.Value >= range.FirstCodePoint && rune.Value < (range.FirstCodePoint + range.Length);
        }

        public virtual string Replace(Rune rune)
        {
            if (CasingTransformation == CasingTransformation.ToLowerCase)
            {
                rune = Culture == null ? Rune.ToLowerInvariant(rune) : Rune.ToLower(rune, Culture);
            }
            else if (CasingTransformation == CasingTransformation.ToUpperCase)
            {
                rune = Culture == null ? Rune.ToUpperInvariant(rune) : Rune.ToUpper(rune, Culture);
            }

            return rune.ToString();
        }
    }
}
