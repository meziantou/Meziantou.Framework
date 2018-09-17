using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Meziantou.Framework.Versioning
{
    /// <summary>
    /// Implements Semantic Version 2.0.0. <see cref="https://semver.org/"/>
    /// </summary>
    // https://github.com/semver/semver/blob/master/semver.md
    // https://github.com/semver/semver/blob/master/semver.svg
    public class SemanticVersion : IFormattable, IComparable, IComparable<SemanticVersion>, IEquatable<SemanticVersion>
    {
        private static readonly IReadOnlyList<string> s_emptyArray = Array.Empty<string>();

        public SemanticVersion(int major, int minor, int patch)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
        }

        public SemanticVersion(int major, int minor, int patch, string prereleaseLabel)
            : this(major, minor, patch, prereleaseLabel, null)
        {
        }

        public SemanticVersion(int major, int minor, int patch, string prereleaseLabel, string metadata)
            : this(major, minor, patch)
        {
            if (prereleaseLabel != null)
            {
                var index = 0;
                PrereleaseLabels = ReadPrereleaseIdentifiers(prereleaseLabel, ref index);
                if (index != prereleaseLabel.Length)
                    throw new ArgumentException("Value is not valid", nameof(prereleaseLabel));
            }

            if (metadata != null)
            {
                var index = 0;
                Metadata = TryReadMetadataIdentifiers(metadata, ref index);
                if (index != metadata.Length)
                    throw new ArgumentException("Value is not valid", nameof(metadata));
            }
        }

        public SemanticVersion(int major, int minor, int patch, IEnumerable<string> prereleaseLabel, IEnumerable<string> metadata)
            : this(major, minor, patch)
        {
            if (prereleaseLabel != null)
            {
                var labels = new ReadOnlyList<string>();
                foreach (var label in prereleaseLabel)
                {
                    if (!IsPrereleaseIdentifier(label))
                        throw new ArgumentException($"Label '{label}' is not valid", nameof(prereleaseLabel));

                    labels.Add(label);
                }

                if (labels.Count > 0)
                {
                    labels.Freeze();
                    PrereleaseLabels = labels;
                }
            }

            if (metadata != null)
            {
                var labels = new ReadOnlyList<string>();
                foreach (var label in metadata)
                {
                    if (!IsMetadataIdentifier(label))
                        throw new ArgumentException($"Label '{label}' is not valid", nameof(metadata));

                    labels.Add(label);
                }

                if (labels.Count > 0)
                {
                    labels.Freeze();
                    Metadata = labels;
                }
            }
        }

        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }

        public virtual IReadOnlyList<string> PrereleaseLabels { get; } = s_emptyArray;
        public virtual bool IsPrerelease => PrereleaseLabels != s_emptyArray;

        public virtual IReadOnlyList<string> Metadata { get; } = s_emptyArray;
        public virtual bool HasMetadata => Metadata != s_emptyArray;

        public virtual string ToString(string format, IFormatProvider formatProvider)
        {
            var sb = new StringBuilder();
            sb.Append(Major);
            sb.Append('.');
            sb.Append(Minor);
            sb.Append('.');
            sb.Append(Patch);
            if (IsPrerelease)
            {
                sb.Append('-');
                var first = true;
                foreach (var label in PrereleaseLabels)
                {
                    if (!first)
                    {
                        sb.Append('.');
                    }

                    sb.Append(label);
                    first = false;
                }
            }

            if (HasMetadata)
            {
                sb.Append('+');
                var first = true;
                foreach (var label in Metadata)
                {
                    if (!first)
                    {
                        sb.Append('.');
                    }

                    sb.Append(label);
                    first = false;
                }
            }

            return sb.ToString();
        }

        public override string ToString()
        {
            return ToString(null, null);
        }

        public override int GetHashCode()
        {
            return SemanticVersionComparer.Instance.GetHashCode(this);
        }

        public override bool Equals(object obj)
        {
            if (obj is SemanticVersion semver)
            {
                return Equals(semver);
            }

            return false;
        }

        public virtual bool Equals(SemanticVersion other)
        {
            return SemanticVersionComparer.Instance.Equals(this, other);
        }

        public virtual int CompareTo(object obj)
        {
            if (obj is SemanticVersion semver)
            {
                return CompareTo(semver);
            }

            throw new ArgumentException("Argument must be an instance of " + nameof(SemanticVersion), nameof(obj));
        }

        public virtual int CompareTo(SemanticVersion other)
        {
            return SemanticVersionComparer.Instance.Compare(this, other);
        }

        public static SemanticVersion Parse(string versionString)
        {
            if (versionString == null)
                throw new ArgumentNullException(nameof(versionString));

            if (TryParse(versionString, out var result))
                return result;

            throw new ArgumentException("The value is not a valid semantic version", nameof(versionString));
        }

        public static bool TryParse(string versionString, out SemanticVersion version)
        {
            // 1.2.3
            // v1.2.3
            // 1.2.3-alpha
            // 1.2.3-alpha.1
            // 1.2.3-apha.1+build
            // 1.2.3-apha.1+build.1.2
            // 1.2.3+build
            // 1.2.3+build.1.2

            version = default;
            if (string.IsNullOrEmpty(versionString))
                return false;

            var index = versionString[0] == 'v' || versionString[0] == 'V' ? 1 : 0;
            if (!TryReadNumber(versionString, ref index, out var major))
                return false;

            if (!TryReadDot(versionString, ref index))
                return false;

            if (!TryReadNumber(versionString, ref index, out var minor))
                return false;

            if (!TryReadDot(versionString, ref index))
                return false;

            if (!TryReadNumber(versionString, ref index, out var patch))
                return false;

            if (!TryReadPrerelease(versionString, ref index, out var prereleaseLabels))
                return false;

            if (!TryReadMetadata(versionString, ref index, out var metadata))
                return false;

            // Should be at the end of the string
            if (index != versionString.Length)
                return false;

            version = new SemanticVersion(major, minor, patch, prereleaseLabels, metadata);
            return true;
        }

        private static bool TryReadDot(string versionString, ref int index)
        {
            if (index < versionString.Length && versionString[index] == '.')
            {
                index++;
                return true;
            }

            return false;
        }

        private static bool TryReadPrerelease(string versionString, ref int index, out IReadOnlyList<string> labels)
        {
            if (index < versionString.Length && versionString[index] == '-')
            {
                index++;

                labels = ReadPrereleaseIdentifiers(versionString, ref index);
                return true;
            }

            labels = null;
            return true;
        }

        private static IReadOnlyList<string> ReadPrereleaseIdentifiers(string versionString, ref int index)
        {
            var result = new List<string>();
            while (true)
            {
                if (TryReadPrereleaseIdentifier(versionString, ref index, out var label))
                {
                    result.Add(label);
                }

                if (!TryReadDot(versionString, ref index))
                    break;
            }

            return result.Count == 0 ? s_emptyArray : ReadOnlyList.From(result);
        }

        private static bool TryReadMetadata(string versionString, ref int index, out IReadOnlyList<string> labels)
        {
            if (index < versionString.Length && versionString[index] == '+')
            {
                index++;

                labels = TryReadMetadataIdentifiers(versionString, ref index);
                return true;
            }

            labels = null;
            return true;
        }

        private static IReadOnlyList<string> TryReadMetadataIdentifiers(string versionString, ref int index)
        {
            var result = new List<string>();
            while (true)
            {
                if (TryReadMetadataIdentifier(versionString, ref index, out var label))
                {
                    result.Add(label);
                }

                if (!TryReadDot(versionString, ref index))
                    break;
            }

            return result.Count == 0 ? s_emptyArray : ReadOnlyList.From(result);
        }

        private static bool IsPrereleaseIdentifier(string label)
        {
            if (label == null)
                return false;

            var index = 0;
            return TryReadPrereleaseIdentifier(label, ref index, out _) && index == label.Length;
        }

        private static bool IsMetadataIdentifier(string label)
        {
            if (label == null)
                return false;

            var index = 0;
            return TryReadMetadataIdentifier(label, ref index, out _) && index == label.Length;
        }

        private static bool TryReadPrereleaseIdentifier(string versionString, ref int index, out string value)
        {
            var last = index;
            while (last < versionString.Length && IsValidLabelCharacter(versionString[last]))
            {
                last++;
            }

            if (last > index)
            {
                value = versionString.Substring(index, last - index);
                if (value[0] != '0' || value.Any(c => !IsDigit(c)))
                {
                    index = last;
                    return true;
                }
            }

            value = default;
            return false;

            bool IsValidLabelCharacter(char c)
            {
                return IsLetter(c) || IsDigit(c) || IsDash(c);
            }
        }

        private static bool TryReadMetadataIdentifier(string versionString, ref int index, out string value)
        {
            var last = index;
            while (last < versionString.Length && IsValidLabelCharacter(versionString[last]))
            {
                last++;
            }

            if (last > index)
            {
                value = versionString.Substring(index, last - index);
                index = last;
                return true;
            }

            value = default;
            return false;

            bool IsValidLabelCharacter(char c)
            {
                return IsLetter(c) || IsDigit(c) || IsDash(c);
            }
        }

        private static bool TryReadNumber(string versionString, ref int index, out int value)
        {
            var last = index;
            while (last < versionString.Length && IsDigit(versionString[last]))
            {
                last++;
            }

            if (last > index)
            {
                var str = versionString.Substring(index, last - index);
                if (str == "0")
                {
                    value = 0;
                    index = last;
                    return true;
                }

                if (str[0] != '0' && int.TryParse(str, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                {
                    value = n;
                    index = last;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }

        private static bool IsLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        private static bool IsDash(char c)
        {
            return c == '-';
        }

        public static bool operator ==(SemanticVersion left, SemanticVersion right) => Equals(left, right);

        public static bool operator !=(SemanticVersion left, SemanticVersion right) => !(left == right);

        public static bool operator <(SemanticVersion left, SemanticVersion right) => SemanticVersionComparer.Instance.Compare(left, right) < 0;

        public static bool operator <=(SemanticVersion left, SemanticVersion right) => SemanticVersionComparer.Instance.Compare(left, right) <= 0;

        public static bool operator >(SemanticVersion left, SemanticVersion right) => SemanticVersionComparer.Instance.Compare(left, right) > 0;

        public static bool operator >=(SemanticVersion left, SemanticVersion right) => SemanticVersionComparer.Instance.Compare(left, right) >= 0;
    }
}
