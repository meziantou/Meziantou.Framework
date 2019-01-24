using System;
using System.Collections.Generic;
using System.Globalization;

namespace Meziantou.Framework
{
    /// <summary>
    /// A utility class to compute unique short names for a given collection of names.
    /// </summary>
    public static class ShortName
    {
        public static string Create(ISet<string> shortNames, int maxLength, string name)
        {
            if (shortNames == null) throw new ArgumentNullException(nameof(shortNames));

            var shortName = name.Substring(0, (name.Length < maxLength) ? name.Length : maxLength);
            var number = 0;
            var pos = maxLength;
            string oldName = null;
            while (shortNames.Contains(shortName))
            {
                if ((name.Length <= maxLength) || (pos >= name.Length))
                {
                    var sn = number.ToString(CultureInfo.InvariantCulture);
                    if (sn.Length < maxLength)
                    {
                        shortName = name.Substring(0, (name.Length < (maxLength - sn.Length)) ? name.Length : (maxLength - sn.Length)) + sn;
                    }
                    number++;
                }
                else
                {
                    shortName = name.Substring(0, maxLength - 1) + name[pos];
                    pos++;
                }

                if (number > 1 && string.Equals(oldName, shortName, StringComparison.Ordinal))
                    return null;

                oldName = shortName;
            }

            return shortName;
        }

        /// <summary>
        /// Create a short name, making sure it does not collide with an existing collection of short names.
        /// </summary>
        /// <param name="shortNames">The list of existing short names.</param>
        /// <param name="maxLength">Maximum length of computed short name.</param>
        /// <param name="name">The shorten name.</param>
        /// <returns>A string representing the short name; <c>null</c> if the short name cannot be created</returns>
        public static string Create(IEnumerable<string> shortNames, int maxLength, string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));

            HashSet<string> dict;
            if (shortNames is HashSet<string> hashSet)
            {
                dict = hashSet;
            }
            else
            {
                dict = new HashSet<string>(shortNames, StringComparer.CurrentCultureIgnoreCase);
            }

            return Create(dict, maxLength, name);
        }

        /// <summary>
        /// Builds a short names collection.
        /// </summary>
        /// <param name="names">The input collection of names to shorten. May not be null.</param>
        /// <param name="maxLength">Maximum length of computed short names.</param>
        /// <returns>A dictionary of shorten names</returns>
        public static IDictionary<string, string> Create(IEnumerable<string> names, int maxLength)
        {
            return Create(names, maxLength, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds a short names collection.
        /// </summary>
        /// <param name="names">The input collection of names to shorten. May not be null.</param>
        /// <param name="maxLength">Maximum length of computed short names.</param>
        /// <param name="comparer">Comparer use to compare short names</param>
        /// <returns>A dictionary of shorten names</returns>
        public static IDictionary<string, string> Create(IEnumerable<string> names, int maxLength, IEqualityComparer<string> comparer)
        {
            if (names == null) throw new ArgumentNullException(nameof(names));

            var shortNames = new Dictionary<string, string>(comparer);
            var dict = new HashSet<string>(names, comparer);
            foreach (var name in names)
            {
                if (name == null)
                    throw new ArgumentException(message: null, nameof(names));

                dict.Remove(name);
                var shortName = Create(dict, maxLength, name);
                dict.Add(shortName);
                shortNames.Add(name, shortName);
            }
            return shortNames;
        }
    }
}
