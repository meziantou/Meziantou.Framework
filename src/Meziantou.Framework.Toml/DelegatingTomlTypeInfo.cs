using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml;

internal sealed class DelegatingTomlTypeInfo<T> : TomlTypeInfo<T>
{
    private readonly TomlTypeInfo _typeInfo;

    public DelegatingTomlTypeInfo(TomlTypeInfo typeInfo) : base(typeInfo.Options)
    {
        _typeInfo = typeInfo;
    }

    public override bool WritesTable => _typeInfo.WritesTable;

    public override void Write(TomlWriter writer, T value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _typeInfo.Write(writer, value);
    }

    public override T? Read(TomlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var value = _typeInfo.ReadAsObject(reader);
        return value is null ? default : (T)value;
    }

    public override object? ReadInto(TomlReader reader, object? existingValue)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return _typeInfo.ReadInto(reader, existingValue);
    }
}
