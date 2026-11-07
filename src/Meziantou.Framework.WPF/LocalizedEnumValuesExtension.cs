using System.Windows.Markup;

namespace Meziantou.Framework.WPF;

/// <summary>
/// XAML markup extension that provides localized enum values with display names from <see cref="System.ComponentModel.DataAnnotations.DisplayAttribute"/>.
/// </summary>
/// <example>
/// <code language="xml" xmlns:ref="urn:meziantou:file-referencer" ref:source="../../samples/Meziantou.Framework.WPF.EnumComboBox/Examples/LocalizedEnumValuesExample.xaml">
/// &lt;UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
///              xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
///              xmlns:wpf="clr-namespace:Meziantou.Framework.WPF;assembly=Meziantou.Framework.WPF"
///              xmlns:local="clr-namespace:Meziantou.Framework.WPF.EnumComboBox"&gt;
///     &lt;!-- Displays the names from the [Display] attributes of the enum members --&gt;
///     &lt;ComboBox ItemsSource="{wpf:LocalizedEnumValues {x:Type local:Week}}" DisplayMemberPath="Name" SelectedValuePath="Value" /&gt;
/// &lt;/UserControl&gt;
/// </code>
/// </example>
[MarkupExtensionReturnType(typeof(IEnumerable<LocalizedEnumValue>))]
public sealed class LocalizedEnumValuesExtension : MarkupExtension
{
    /// <summary>Initializes a new instance of the <see cref="LocalizedEnumValuesExtension"/> class.</summary>
    public LocalizedEnumValuesExtension()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="LocalizedEnumValuesExtension"/> class with the specified enum type.</summary>
    /// <param name="enumType">The enum type.</param>
    public LocalizedEnumValuesExtension(Type enumType)
    {
        EnumType = enumType;
    }

    /// <summary>Gets or sets the enum type to get localized values from.</summary>
    [ConstructorArgument("enumType")]
    public Type? EnumType { get; set; }

    /// <summary>Returns the localized enum values.</summary>
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (EnumType is null)
            throw new InvalidOperationException("The enum type is not set");

        if (!EnumType.IsEnum)
            throw new InvalidOperationException($"'{EnumType}' is not an enum type");

        return EnumLocalizationUtilities.GetEnumLocalization(EnumType);
    }
}
