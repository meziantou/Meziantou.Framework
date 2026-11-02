using System.ComponentModel.DataAnnotations;

namespace Meziantou.Framework.DataAnnotations;

/// <summary>
/// The result returned by <see cref="ValidateCollectionItemsAttribute"/> when at least one item of the collection is invalid.
/// </summary>
/// <remarks>
/// <see cref="ValidationResult.ErrorMessage"/> and <see cref="ValidationResult.MemberNames"/> describe the first error.
/// <see cref="Errors"/> holds every error of every item.
/// </remarks>
public sealed class CollectionItemsValidationResult : ValidationResult
{
    internal CollectionItemsValidationResult(string? errorMessage, IEnumerable<string> memberNames, IReadOnlyList<ValidationResult> errors)
        : base(errorMessage, memberNames)
    {
        Errors = errors;
    }

    /// <summary>
    /// Gets the errors of all the invalid items. Their member names are prefixed with the name of the collection and the index of the item, such as <c>Items[1].Name</c>.
    /// </summary>
    public IReadOnlyList<ValidationResult> Errors { get; }
}
