using Meziantou.Framework.NuGetPackageValidation.Rules;

namespace Meziantou.Framework.NuGetPackageValidation;

public static class NuGetPackageValidator
{
    public static Task<NuGetPackageValidationResult> ValidateAsync(FullPath packagePath, CancellationToken cancellationToken = default)
    {
        return ValidateAsync(packagePath, NuGetPackageValidationRules.Default, cancellationToken);
    }

    public static async Task<NuGetPackageValidationResult> ValidateAsync(FullPath packagePath, IEnumerable<NuGetPackageValidationRule> rules, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(packagePath))
        {
            return new NuGetPackageValidationResult(packagePath, new NuGetPackageValidationError[]
            {
                new (ErrorCodes.FileNotFound, $"NuGet package '{packagePath}' not found"),
            });
        }

        using var context = new NuGetPackageValidationContext(packagePath, cancellationToken);
        foreach (var rule in rules)
        {
            await rule.ExecuteAsync(context).ConfigureAwait(false);
        }

        return new NuGetPackageValidationResult(packagePath, context.Errors);
    }
}
