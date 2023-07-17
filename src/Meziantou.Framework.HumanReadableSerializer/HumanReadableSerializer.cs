namespace Meziantou.Framework.HumanReadable;

public static class HumanReadableSerializer
{
    public static string Serialize(object? value, HumanReadableSerializerOptions? options = null)
    {
        options ??= new HumanReadableSerializerOptions();
        var writer = new HumanReadableTextWriter(options);
        Serialize(writer, value, value?.GetType() ?? typeof(object), options);
        return writer.ToString();
    }

    public static string Serialize(object? value, Type type, HumanReadableSerializerOptions? options = null)
    {
        if (value != null && !type.IsAssignableFrom(value.GetType()))
            throw new ArgumentException($"The provided value cannot be assigned to type '{type.AssemblyQualifiedName}'", nameof(value));

        options ??= new HumanReadableSerializerOptions();
        var writer = new HumanReadableTextWriter(options);
        Serialize(writer, value, type, options);
        return writer.ToString();
    }

    public static void Serialize(HumanReadableTextWriter writer, object? value, Type type, HumanReadableSerializerOptions options)
    {
        using (options.BeginScope())
        {
            var converter = options.GetConverter(type);
            converter.WriteValue(writer, value, options);
        }
    }

    public static void Serialize<T>(HumanReadableTextWriter writer, T? value, HumanReadableSerializerOptions options)
    {
        Serialize(writer, value, value?.GetType() ?? typeof(T), options);
    }
}
