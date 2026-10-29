using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Meziantou.Framework.FastEnumGenerator.GeneratorTests;

#region FastEnum_MetadataEnum
public enum Color
{
    [Display(Name = "Blue metadata")]
    Blue,

    [EnumMember(Value = "Red metadata")]
    Red,

    Green,
}
#endregion
