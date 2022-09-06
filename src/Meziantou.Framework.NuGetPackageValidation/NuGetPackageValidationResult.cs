namespace Meziantou.Framework.NuGetPackageValidation;

public sealed class NuGetPackageValidationResult
{
    public NuGetPackageValidationResult(FullPath file, IReadOnlyCollection<NuGetPackageValidationError> errors)
    {
        File = file;
        Errors = errors;
    }

    public FullPath File { get; }
    public IReadOnlyCollection<NuGetPackageValidationError> Errors { get; }

    public bool IsValid => Errors.Count == 0;
}
