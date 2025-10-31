namespace Meziantou.Framework.NuGetPackageValidation;

/// <summary>Represents the result of a NuGet package validation operation.</summary>
public sealed class NuGetPackageValidationResult
{
    /// <summary>Initializes a new instance of the <see cref="NuGetPackageValidationResult"/> class.</summary>
    /// <param name="errors">The collection of validation errors found during the validation process.</param>
    public NuGetPackageValidationResult(IReadOnlyCollection<NuGetPackageValidationError> errors)
    {
        Errors = errors;
    }

    /// <summary>Gets the collection of validation errors found during the validation process.</summary>
    public IReadOnlyCollection<NuGetPackageValidationError> Errors { get; }

    /// <summary>Gets a value indicating whether the package passed all validation rules without any errors.</summary>
    public bool IsValid => Errors.Count == 0;
}
