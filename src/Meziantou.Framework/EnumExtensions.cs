#pragma warning disable CA1000 // Do not declare static members on generic types
namespace Meziantou.Framework;

public static class EnumExtensions
{
    extension<T>(T) where T : struct, Enum
    {
        public static T Parse(string value)
            => Enum.Parse<T>(value);

        public static T Parse(string value, bool ignoreCase)
            => Enum.Parse<T>(value, ignoreCase);

        public static T Parse(ReadOnlySpan<char> value)
            => Enum.Parse<T>(value);

        public static T Parse(ReadOnlySpan<char> value, bool ignoreCase)
            => Enum.Parse<T>(value, ignoreCase);

        public static bool TryParse([NotNullWhen(true)] string? value, bool ignoreCase, out T result)
            => Enum.TryParse(value, ignoreCase, out result);

        public static bool TryParse(ReadOnlySpan<char> value, bool ignoreCase, out T result)
            => Enum.TryParse(value, ignoreCase, out result);
    }
}
