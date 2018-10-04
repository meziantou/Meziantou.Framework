using System;
using System.Text;
using System.Diagnostics.Contracts;

namespace Meziantou.Framework
{
    public static class StringBuilderUtilities
    {
        [Pure]
        public static bool StartsWith(this StringBuilder stringBuilder, char prefix)
        {
            if (stringBuilder == null)
                throw new ArgumentNullException(nameof(stringBuilder));

            if (stringBuilder.Length == 0)
                return false;

            return stringBuilder[0] == prefix;
        }

        [Pure]
        public static bool StartsWith(this StringBuilder stringBuilder, string prefix)
        {
            if (stringBuilder == null)
                throw new ArgumentNullException(nameof(stringBuilder));
            if (prefix == null)
                throw new ArgumentNullException(nameof(prefix));

            if (stringBuilder.Length < prefix.Length)
                return false;

            for (var i = 0; i < prefix.Length; i++)
            {
                if (stringBuilder[i] != prefix[i])
                    return false;
            }

            return true;
        }

        [Pure]
        public static bool EndsWith(this StringBuilder stringBuilder, char suffix)
        {
            if (stringBuilder == null)
                throw new ArgumentNullException(nameof(stringBuilder));

            if (stringBuilder.Length == 0)
                return false;

            return stringBuilder[stringBuilder.Length - 1] == suffix;
        }

        [Pure]
        public static bool EndsWith(this StringBuilder stringBuilder, string suffix)
        {
            if (stringBuilder == null)
                throw new ArgumentNullException(nameof(stringBuilder));
            if (suffix == null)
                throw new ArgumentNullException(nameof(suffix));

            if (stringBuilder.Length < suffix.Length)
                return false;

            for (var index = 0; index < suffix.Length; index++)
            {
                if (stringBuilder[stringBuilder.Length - 1 - index] != suffix[suffix.Length - 1 - index])
                    return false;
            }

            return true;
        }
    }
}
