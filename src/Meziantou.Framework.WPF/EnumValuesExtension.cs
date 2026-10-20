using System.Windows.Markup;

namespace Meziantou.Framework.WPF;

/// <summary>XAML markup extension that provides all values of an enum type.</summary>
/// <example>
/// <code language="xml" xmlns:ref="urn:meziantou:file-referencer" ref:source="../../samples/Meziantou.Framework.WPF.EnumComboBox/Examples/EnumValuesExample.xaml">
/// &lt;UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
///              xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
///              xmlns:wpf="clr-namespace:Meziantou.Framework.WPF;assembly=Meziantou.Framework.WPF"
///              xmlns:local="clr-namespace:Meziantou.Framework.WPF.EnumComboBox"&gt;
///     &lt;ComboBox ItemsSource="{wpf:EnumValues {x:Type local:Week}}" /&gt;
/// &lt;/UserControl&gt;
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
        if (EnumType is null)
            throw new InvalidOperationException("The enum type is not set");

        if (!EnumType.IsEnum)
            throw new InvalidOperationException($"'{EnumType}' is not an enum type");

        return Enum.GetValues(EnumType);
    }
}
