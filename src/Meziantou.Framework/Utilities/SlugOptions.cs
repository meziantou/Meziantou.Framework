using System.Collections.Generic;
using System.Globalization;

namespace Meziantou.Framework.Utilities
{
    public class SlugOptions
    {
        public const int DefaultMaximumLength = 80;
        public const string DefaultSeparator = "-";
        private bool _toLower;
        private bool _toUpper;

        public IList<UnicodeCategory> AllowedUnicodeCategories { get; }

        public IList<Range<char>> AllowedRanges { get; }

        public int MaximumLength { get; set; }

        public string Separator { get; set; }

        public CultureInfo Culture { get; set; }

        public bool CanEndWithSeparator { get; set; }

        public bool EarlyTruncate { get; set; }

        public bool ToLower
        {
            get
            {
                return _toLower;
            }
            set
            {
                _toLower = value;
                if (!_toLower)
                    return;
                _toUpper = false;
            }
        }

        public bool ToUpper
        {
            get
            {
                return _toUpper;
            }
            set
            {
                _toUpper = value;
                if (!_toUpper)
                    return;
                _toLower = false;
            }
        }

        public SlugOptions()
        {
            MaximumLength = DefaultMaximumLength;
            Separator = DefaultSeparator;
            AllowedUnicodeCategories = new List<UnicodeCategory>
            {
                UnicodeCategory.UppercaseLetter,
                UnicodeCategory.LowercaseLetter,
                UnicodeCategory.DecimalDigitNumber
            };
            AllowedRanges = new List<Range<char>>
            {
                Range.Create('a', 'z'),
                Range.Create('A', 'Z'),
                Range.Create('0', '9')
            };
        }

        public virtual bool IsAllowed(char character)
        {
            foreach (var allowedRange in AllowedRanges)
            {
                if (character >= allowedRange.From && character <= allowedRange.To)
                    return true;
            }
            return false;
        }

        public virtual string Replace(char character)
        {
            return character.ToString();
        }
    }
}
