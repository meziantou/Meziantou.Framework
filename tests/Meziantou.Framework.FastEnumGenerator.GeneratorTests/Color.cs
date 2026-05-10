using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Meziantou.Framework.FastEnumGenerator.GeneratorTests;

public enum Color
{
    [Display(Name = "Blue metadata")]
    Blue,

    [EnumMember(Value = "Red metadata")]
    Red,

    Green,
}

public enum ColorWithAliases
{
    Blue = 0,
    Azure = 0,
    Red = 1,
}
