using System.ComponentModel.DataAnnotations;

namespace Meziantou.Framework.FastEnumGenerator.GeneratorTests;

[Flags]
public enum PermissionWithDisplayMetadata
{
    None = 0,

    [Display(Name = "Read metadata")]
    Read = 1,

    [Display(Name = "Write metadata")]
    Write = 2,

    [Display(Name = "Execute metadata")]
    Execute = 4,
}
