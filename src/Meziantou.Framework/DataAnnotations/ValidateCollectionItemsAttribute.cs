using System.Collections;
using System.ComponentModel.DataAnnotations;

namespace Meziantou.Framework.DataAnnotations;

/// <summary>Validates each item in a collection by running validation on each item.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Tests/DataAnnotations/ValidateCollectionItemsAttributeTests.cs" ref:region="ValidateCollectionItemsAttribute_Usage" ref:dedent="true">
/// public sealed class MyModel
/// {
///     // Validates each item using the data annotations of ChildModel
///     [ValidateCollectionItems]
///     public List&lt;ChildModel&gt;? Items { get; set; }
/// }
///
/// public sealed class ChildModel
/// {
///     [Required]
///     public string? Name { get; set; }
/// }
/// </code>
/// </example>
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
                Validator.TryValidateObject(item, itemValidationContext, results, validateAllProperties: true);
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
