using System.ComponentModel.DataAnnotations;

namespace Meziantou.Framework.FastEnumGenerator.FuzzEnums;

[Flags]
public enum FuzzPermission : byte
{
    None = 0,

    [Display(Name = "read access")]
    Read = 1,

    Write = 2,
    Execute = 4,
    ReadWrite = 3,
    All = 7,
}
