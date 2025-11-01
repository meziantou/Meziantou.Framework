using System.Windows.Markup;

namespace Meziantou.Framework.WPF;

/// <summary>
/// XAML markup extension that provides localized enum values with display names from <see cref="System.ComponentModel.DataAnnotations.DisplayAttribute"/>.
/// </summary>
/// <example>
/// <code>
/// &lt;ComboBox ItemsSource="{wpf:LocalizedEnumValues {x:Type MyEnum}}" DisplayMemberPath="Name" SelectedValuePath="Value" /&gt;
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
        if (EnumType == null)
            throw new InvalidOperationException("The enum type is not set");

        return EnumLocalizationUtilities.GetEnumLocalization(EnumType);
    }
}
