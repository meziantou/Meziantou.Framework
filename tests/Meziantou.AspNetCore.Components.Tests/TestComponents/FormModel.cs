using System.ComponentModel.DataAnnotations;

namespace Meziantou.AspNetCore.Components.Tests.TestComponents;

public sealed class FormModel
{
    [Required]
    [Display(Name = "Full name", Order = 1)]
    public string? Name { get; set; }

    [Range(1, 120)]
    [Display(Order = 2)]
    public int Age { get; set; }

    [Display(Name = "Subscribe to the newsletter", Order = 3)]
    public bool Subscribe { get; set; }

    [Display(Order = 4)]
    public DayOfWeek? Day { get; set; }

    [DataType(DataType.MultilineText)]
    [Display(Order = 5)]
    public string? Comment { get; set; }
}
