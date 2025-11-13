using System.ComponentModel.DataAnnotations;

namespace Meziantou.Framework.DataAnnotations;

/// <summary>Validates that a string property contains an absolute URI.</summary>
/// <example>
/// <code>
/// public class MyModel
/// {
///     [AbsoluteUri]
///     public string? Url { get; set; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AbsoluteUriAttribute : ValidationAttribute
{
    public AbsoluteUriAttribute()
    {
    }

    public AbsoluteUriAttribute(string errorMessage)
        : base(errorMessage)
    {
    }

    public override bool IsValid(object? value)
    {
        if (value is string str)
            return Uri.TryCreate(str, UriKind.Absolute, out _);

        return true;
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (!IsValid(value))
            return new ValidationResult($"Uri '{value}' is not an absolute URI");

        return null;
    }
}
