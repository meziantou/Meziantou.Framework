using System.ComponentModel.DataAnnotations;

namespace Meziantou.Framework.WPF;

/// <summary>Represents an enum value with its localized display name.</summary>
public sealed class LocalizedEnumValue
{
    private readonly DisplayAttribute? _displayAttribute;

    /// <summary>Initializes a new instance of the <see cref="LocalizedEnumValue"/> class using the enum value's string representation as the name.</summary>
    /// <param name="value">The enum value.</param>
    public LocalizedEnumValue(Enum value)
        : this(value, value.ToString())
    {
    }

    /// <summary>Initializes a new instance of the <see cref="LocalizedEnumValue"/> class with a custom name.</summary>
    /// <param name="value">The enum value.</param>
    /// <param name="name">The display name.</param>
    public LocalizedEnumValue(Enum value, string name)
    {
        Value = value;
        Name = name;
    }

    /// <summary>Initializes a new instance of the <see cref="LocalizedEnumValue"/> class using a <see cref="DisplayAttribute"/> for localization.</summary>
    /// <param name="value">The enum value.</param>
    /// <param name="displayAttribute">The display attribute containing localization information.</param>
    public LocalizedEnumValue(Enum value, DisplayAttribute displayAttribute)
    {
        Value = value;
        _displayAttribute = displayAttribute;
    }

    /// <summary>Gets the localized display name of the enum value.</summary>
    public string Name
    {
        get
        {
            if (_displayAttribute is not null)
                return _displayAttribute.GetName();

            return field!;
        }
    }

    /// <summary>Gets the enum value.</summary>
    public object Value { get; private set; }

    /// <summary>Returns the localized display name.</summary>
    public override string ToString() => Name;
}
