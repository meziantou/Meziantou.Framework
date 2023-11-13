using System.ComponentModel.DataAnnotations;

namespace Meziantou.Framework.WPF;

public sealed class LocalizedEnumValue
{
    private readonly string? _name;
    private readonly DisplayAttribute? _displayAttribute;

    public LocalizedEnumValue(Enum value)
        : this(value, value.ToString())
    {
    }

    public LocalizedEnumValue(Enum value, string name)
    {
        Value = value;
        _name = name;
    }

    public LocalizedEnumValue(Enum value, DisplayAttribute displayAttribute)
    {
        Value = value;
        _displayAttribute = displayAttribute;
    }

    public string Name
    {
        get
        {
            if (_displayAttribute is not null)
                return _displayAttribute.GetName();

            return _name!;
        }
    }

    public object Value { get; private set; }

    public override string ToString() => Name;
}
