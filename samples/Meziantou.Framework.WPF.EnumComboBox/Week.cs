using System.ComponentModel.DataAnnotations;

namespace Meziantou.Framework.WPF.EnumComboBox;

#region LocalizedEnumValuesExtension_Enum
public enum Week
{
    [Display(ResourceType = typeof(TestEnumResources), Name = "TestEnum_First")]
    First,

    [Display(ResourceType = typeof(TestEnumResources), Name = "TestEnum_Second")]
    Second,

    [Display(ResourceType = typeof(TestEnumResources), Name = "TestEnum_Third")]
    Third,

    [Display(ResourceType = typeof(TestEnumResources), Name = "TestEnum_Fourth")]
    Fourth,

    [Display(ResourceType = typeof(TestEnumResources), Name = "TestEnum_Last")]
    Last,
}
#endregion
