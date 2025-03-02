using System.Collections;
using System.ComponentModel.DataAnnotations;

namespace Meziantou.Framework.DataAnnotations;

[AttributeUsage(AttributeTargets.Property)]
[RequiresUnreferencedCode("The Type of instance cannot be statically discovered and the Type's properties can be trimmed.")]
public sealed class ValidateCollectionItemsAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                var results = new List<ValidationResult>();
                var itemValidationContext = new ValidationContext(item);
                Validator.TryValidateObject(item, itemValidationContext, results);
                if (results.Count > 0)
                    return results[0];
            }
        }

        return null;
    }

    public override bool IsValid(object? value)
    {
        if (value is null)
            return true;

        return IsValid(value, new ValidationContext(value))?.ErrorMessage is null;
    }
}
