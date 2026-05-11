using System.ComponentModel.DataAnnotations;

namespace FastEnumGeneratorBenchmarks;

[Flags]
public enum MetadataFlagsEnum
{
    None = 0,

    [Display(Name = "Read metadata")]
    Read = 1,

    [Display(Name = "Write metadata")]
    Write = 2,

    [Display(Name = "Execute metadata")]
    Execute = 4,

    [Display(Name = "Delete metadata")]
    Delete = 8,
}
