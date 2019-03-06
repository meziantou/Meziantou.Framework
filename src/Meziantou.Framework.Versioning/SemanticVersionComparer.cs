using System;
using System.Collections.Generic;
using System.Globalization;

namespace Meziantou.Framework.Versioning
{
    internal class SemanticVersionComparer : IComparer<SemanticVersion>, IEqualityComparer<SemanticVersion>
    {
        public static SemanticVersionComparer Instance { get; } = new SemanticVersionComparer();

        public int Compare(SemanticVersion x, SemanticVersion y)
        {
            if (ReferenceEquals(x, y))
                return 0;

            if (x == null)
                return -1;

            if (y == null)
                return 1;

            var result = x.Major.CompareTo(y.Major);
            if (result != 0)
                return result;

            result = x.Minor.CompareTo(y.Minor);
            if (result != 0)
                return result;

            result = x.Patch.CompareTo(y.Patch);
            if (result != 0)
                return result;

            if (!x.IsPrerelease && !y.IsPrerelease)
                return 0;

            if (x.IsPrerelease && !y.IsPrerelease)
                return -1;

            if (!x.IsPrerelease && y.IsPrerelease)
                return 1;

            for (var i = 0; i < x.PrereleaseLabels.Count && i < y.PrereleaseLabels.Count; i++)
            {
                var left = x.PrereleaseLabels[i];
                var right = y.PrereleaseLabels[i];

                var isLeftNumber = int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
                var isRightNumber = int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);

                if (isLeftNumber && isRightNumber)
                {
                    result = leftNumber.CompareTo(rightNumber);
                    if (result != 0)
                        return result;
                }
                else
                {
                    if (isLeftNumber)
                        return -1;

                    if (isRightNumber)
                        return 1;

                    result = StringComparer.Ordinal.Compare(left, right);
                    if (result != 0)
                        return result;
                }
            }

            if (x.PrereleaseLabels.Count > y.PrereleaseLabels.Count)
                return 1;

            if (x.PrereleaseLabels.Count < y.PrereleaseLabels.Count)
                return -1;

            return 0;
        }

        public bool Equals(SemanticVersion x, SemanticVersion y)
        {
            return Compare(x, y) == 0;
        }

        public int GetHashCode(SemanticVersion obj)
        {
            if (obj == null)
                return 0;

            var hash = obj.Major.GetHashCode();
            hash = (hash * 397) ^ obj.Minor.GetHashCode();
            hash = (hash * 397) ^ obj.Patch.GetHashCode();
            if (obj.IsPrerelease)
            {
                for (var i = 0; i < obj.PrereleaseLabels.Count; i++)
                {
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(obj.PrereleaseLabels[i]);
                }
            }

            return hash;
        }
    }
}
