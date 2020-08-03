using System.ComponentModel.DataAnnotations;

namespace Meziantou.Framework.WPF.EnumComboBox
{
    public enum Week
    {
        [Display(ResourceType = typeof(Resources), Name = "TestEnum_First")]
        First,

        [Display(ResourceType = typeof(Resources), Name = "TestEnum_Second")]
        Second,

        [Display(ResourceType = typeof(Resources), Name = "TestEnum_Third")]
        Third,

        [Display(ResourceType = typeof(Resources), Name = "TestEnum_Fourth")]
        Fourth,

        [Display(ResourceType = typeof(Resources), Name = "TestEnum_Last")]
        Last
    }

    public  class Resources
    {
        public static string TestEnum_First => "Première"; 
        public static string TestEnum_Second => "Deuxième"; 
        public static string TestEnum_Third => "Troisième"; 
        public static string TestEnum_Fourth => "Quatrième"; 
        public static string TestEnum_Last => "Dernière"; 
    }
}
