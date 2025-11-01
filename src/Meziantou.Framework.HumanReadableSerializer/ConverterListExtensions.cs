namespace Meziantou.Framework.HumanReadable;

/// <summary>Provides extension methods for adding converters to a converter list.</summary>
public static class ConverterListExtensions
{
    /// <summary>Adds a converter using a conversion function.</summary>
    /// <typeparam name="T">The type to convert.</typeparam>
    /// <param name="converters">The list of converters.</param>
    /// <param name="convert">The conversion function.</param>
    public static void Add<T>(this IList<HumanReadableConverter> converters, Func<T, string> convert)
    {
        converters.Add(new FuncConverter<T>((value, options) => convert(value)));
    }

    /// <summary>Adds a converter using a conversion function that accepts serialization options.</summary>
    /// <typeparam name="T">The type to convert.</typeparam>
    /// <param name="converters">The list of converters.</param>
    /// <param name="convert">The conversion function.</param>
    public static void Add<T>(this IList<HumanReadableConverter> converters, Func<T, HumanReadableSerializerOptions, string> convert)
    {
        converters.Add(new FuncConverter<T>(convert));
    }

    private sealed class FuncConverter<T> : HumanReadableConverter<T>
    {
        private readonly Func<T, HumanReadableSerializerOptions, string> _converter;

        public FuncConverter(Func<T, HumanReadableSerializerOptions, string> converter) => _converter = converter;

        protected override void WriteValue(HumanReadableTextWriter writer, T? value, HumanReadableSerializerOptions options)
        {
            var str = _converter(value, options);
            writer.WriteValue(str);
        }
    }
}
