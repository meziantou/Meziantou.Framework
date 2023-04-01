namespace Meziantou.Framework.HumanReadable;

public static class HumanReadableSerializer
{
    public static string Serialize(object? value, HumanReadableSerializerOptions? options = null)
    {
        options ??= new HumanReadableSerializerOptions();

        var writer = new HumanReadableTextWriter(options);
        Serialize(writer, value, options);
        return writer.ToString();
    }

    public static string Serialize<T>(T? value, HumanReadableSerializerOptions? options = null)
    {
        options ??= new HumanReadableSerializerOptions();

        var writer = new HumanReadableTextWriter(options);
        Serialize(writer, value, options);
        return writer.ToString();
    }

    internal static void Serialize(HumanReadableTextWriter writer, object? value, Type type, HumanReadableSerializerOptions options)
    {
        var converter = options.GetConverter(value?.GetType() ?? type);
        converter.WriteValue(writer, value, options);
    }

    internal static void Serialize<T>(HumanReadableTextWriter writer, T? value, HumanReadableSerializerOptions options)
    {
        Serialize(writer, value, typeof(T), options);
    }
}
