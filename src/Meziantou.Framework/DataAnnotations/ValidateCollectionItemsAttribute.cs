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
        if (value is not IEnumerable enumerable)
            return ValidationResult.Success;

        var collectionName = validationContext.MemberName;
        List<ValidationResult>? errors = null;
        var index = 0;
        foreach (var item in enumerable)
        {
            if (item is not null)
            {
                var itemResults = new List<ValidationResult>();
                var itemValidationContext = new ValidationContext(item, validationContext, validationContext.Items);
                if (!Validator.TryValidateObject(item, itemValidationContext, itemResults, validateAllProperties: true))
                {
                    var itemPath = collectionName + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                    foreach (var itemResult in itemResults)
                    {
                        var memberNames = itemResult.MemberNames.Any()
                            ? itemResult.MemberNames.Select(name => itemPath + "." + name).ToArray()
                            : [itemPath];

                        errors ??= [];
                        errors.Add(new ValidationResult(itemResult.ErrorMessage, memberNames));
                    }
                }
            }

            index++;
        }

        if (errors is null)
            return ValidationResult.Success;

        var message = ErrorMessage is null && ErrorMessageResourceName is null
            ? errors[0].ErrorMessage
            : FormatErrorMessage(validationContext.DisplayName);

        var resultMemberNames = ErrorMessage is null && ErrorMessageResourceName is null
            ? errors[0].MemberNames
            : errors.SelectMany(error => error.MemberNames).Distinct(StringComparer.Ordinal);

        return new CollectionItemsValidationResult(message, resultMemberNames, errors);
    }

    public override bool IsValid(object? value)
    {
        if (value is null)
            return true;

        return IsValid(value, new ValidationContext(value)) == ValidationResult.Success;
    }
}
