using System.ComponentModel.DataAnnotations;

namespace Meziantou.Framework.WPF.EnumComboBox
{
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
        Last
    }

    public class TestEnumResources
    {
        public static string TestEnum_First => "Première";
        public static string TestEnum_Second => "Deuxième";
        public static string TestEnum_Third => "Troisième";
        public static string TestEnum_Fourth => "Quatrième";
        public static string TestEnum_Last => "Dernière";
    }
}
