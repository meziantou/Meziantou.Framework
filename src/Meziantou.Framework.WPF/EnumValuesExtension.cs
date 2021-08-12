using System;
using System.Collections.Generic;
using System.Windows.Markup;

namespace Meziantou.Framework.WPF;

[MarkupExtensionReturnType(typeof(IEnumerable<Enum>))]
public sealed class EnumValuesExtension : MarkupExtension
{
    public EnumValuesExtension()
    {
    }

    public EnumValuesExtension(Type enumType)
    {
        EnumType = enumType;
    }

    [ConstructorArgument("enumType")]
    public Type? EnumType { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (EnumType == null)
            throw new InvalidOperationException("The enum type is not set");

        return Enum.GetValues(EnumType);
    }
}
