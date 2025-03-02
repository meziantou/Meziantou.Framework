namespace Meziantou.Framework.HumanReadable;
public static class ConverterListExtensions
{
    public static void Add<T>(this IList<HumanReadableConverter> converters, Func<T, string> convert)
    {
        converters.Add(new FuncConverter<T>((value, options) => convert(value)));
    }

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
