namespace Meziantou.Framework.HumanReadable;

public abstract class HumanReadableConverter<T> : HumanReadableConverter
{
    public sealed override bool CanConvert(Type type) => typeof(T).IsAssignableFrom(type);

    public sealed override void WriteValue(HumanReadableTextWriter writer, object? value, Type valueType, HumanReadableSerializerOptions options)
    {
        WriteValue(writer, (T?)value, options);
    }

    protected abstract void WriteValue(HumanReadableTextWriter writer, T? value, HumanReadableSerializerOptions options);
}
