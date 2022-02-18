using System.Windows.Markup;

namespace Meziantou.Framework.WPF;

[MarkupExtensionReturnType(typeof(IEnumerable<LocalizedEnumValue>))]
public sealed class LocalizedEnumValuesExtension : MarkupExtension
{
    public LocalizedEnumValuesExtension()
    {
    }

    public LocalizedEnumValuesExtension(Type enumType)
    {
        EnumType = enumType;
    }

    [ConstructorArgument("enumType")]
    public Type? EnumType { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (EnumType == null)
            throw new InvalidOperationException("The enum type is not set");

        return EnumLocalizationUtilities.GetEnumLocalization(EnumType);
    }
}
