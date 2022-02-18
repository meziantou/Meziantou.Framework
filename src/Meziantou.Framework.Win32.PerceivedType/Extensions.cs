using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Meziantou.Framework.Win32
{
    internal static class Extensions
    {
        [SupportedOSPlatform("windows")]
        public static string? GetStringValue(this RegistryKey key, string name)
        {
            var value = key.GetValue(name);
            if (value is string str)
            {
                return str;
            }

            return null;
        }

        public static T GetEnumValue<T>(string value, T defaultValue)
            where T : struct
        {
            if (Enum.TryParse<T>(value, ignoreCase: true, out var result))
                return result;

            return defaultValue;
        }
    }
}
