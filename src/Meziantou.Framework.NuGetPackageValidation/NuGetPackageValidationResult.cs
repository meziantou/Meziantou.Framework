namespace Meziantou.Framework.NuGetPackageValidation;

public sealed class NuGetPackageValidationResult
{
    public NuGetPackageValidationResult(IReadOnlyCollection<NuGetPackageValidationError> errors)
    {
        Errors = errors;
    }

    public IReadOnlyCollection<NuGetPackageValidationError> Errors { get; }

    public bool IsValid => Errors.Count == 0;
}
