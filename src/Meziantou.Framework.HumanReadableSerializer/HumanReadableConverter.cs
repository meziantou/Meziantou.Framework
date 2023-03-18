namespace Meziantou.Framework.HumanReadable;

public abstract class HumanReadableConverter
{
    public virtual bool HandleNull { get; }

    public abstract bool CanConvert(Type type);

    public abstract void WriteValue(HumanReadableTextWriter writer, object? value, HumanReadableSerializerOptions options);
}
