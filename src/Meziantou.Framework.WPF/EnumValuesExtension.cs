using System.Windows.Markup;

namespace Meziantou.Framework.WPF;

/// <summary>XAML markup extension that provides all values of an enum type.</summary>
/// <example>
/// <code>
/// &lt;ComboBox ItemsSource="{wpf:EnumValues {x:Type MyEnum}}" /&gt;
/// </code>
/// </example>
[MarkupExtensionReturnType(typeof(IEnumerable<Enum>))]
public sealed class EnumValuesExtension : MarkupExtension
{
    /// <summary>Initializes a new instance of the <see cref="EnumValuesExtension"/> class.</summary>
    public EnumValuesExtension()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="EnumValuesExtension"/> class with the specified enum type.</summary>
    /// <param name="enumType">The enum type.</param>
    public EnumValuesExtension(Type enumType)
    {
        EnumType = enumType;
    }

    /// <summary>Gets or sets the enum type to get values from.</summary>
    [ConstructorArgument("enumType")]
    public Type? EnumType { get; set; }

    /// <summary>Returns the enum values.</summary>
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (EnumType == null)
            throw new InvalidOperationException("The enum type is not set");

        return Enum.GetValues(EnumType);
    }
}
