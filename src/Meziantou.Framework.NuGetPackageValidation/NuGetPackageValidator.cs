using Meziantou.Framework.NuGetPackageValidation.Rules;

namespace Meziantou.Framework.NuGetPackageValidation;

public static class NuGetPackageValidator
{
    public static Task<NuGetPackageValidationResult> ValidateAsync(FullPath packagePath, CancellationToken cancellationToken = default)
    {
        return ValidateAsync(packagePath, NuGetPackageValidationOptions.Default, cancellationToken);
    }

    public static Task<NuGetPackageValidationResult> ValidateAsync(FullPath packagePath, IEnumerable<NuGetPackageValidationRule> rules, CancellationToken cancellationToken = default)
    {
        var options = new NuGetPackageValidationOptions();
        foreach (var rule in rules)
        {
            options.Rules.Add(rule);
        }

        return ValidateAsync(packagePath, options, cancellationToken);
    }

    public static async Task<NuGetPackageValidationResult> ValidateAsync(FullPath packagePath, NuGetPackageValidationOptions options, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(packagePath))
        {
            return new NuGetPackageValidationResult(packagePath, new NuGetPackageValidationError[]
            {
                new (ErrorCodes.FileNotFound, $"NuGet package '{packagePath}' not found"),
            });
        }

        using var context = new NuGetPackageValidationContext(packagePath, options, cancellationToken);
        foreach (var rule in options.Rules)
        {
            await rule.ExecuteAsync(context).ConfigureAwait(false);
        }

        return new NuGetPackageValidationResult(packagePath, context.Errors);
    }
}
