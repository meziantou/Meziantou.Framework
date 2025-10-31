using Meziantou.Framework.NuGetPackageValidation.Rules;

namespace Meziantou.Framework.NuGetPackageValidation;

/// <summary>Provides methods to validate NuGet packages against a set of rules to ensure they follow best practices and contain required metadata.</summary>
/// <example>
/// <code>
/// // Validate a package with default rules
/// var result = await NuGetPackageValidator.ValidateAsync("MyPackage.1.0.0.nupkg");
/// if (!result.IsValid)
/// {
///     foreach (var error in result.Errors)
///     {
///         Console.WriteLine($"{error.ErrorCode}: {error.Message}");
///     }
/// }
///
/// // Validate with specific rules
/// var options = new NuGetPackageValidationOptions();
/// options.Rules.Add(NuGetPackageValidationRules.LicenseMustBeSet);
/// options.Rules.Add(NuGetPackageValidationRules.AuthorMustBeSet);
/// var customResult = await NuGetPackageValidator.ValidateAsync("MyPackage.1.0.0.nupkg", options);
/// </code>
/// </example>
public static class NuGetPackageValidator
{
    /// <summary>Validates a NuGet package using the default set of validation rules.</summary>
    /// <param name="packagePath">The path to the NuGet package file (.nupkg) to validate.</param>
    /// <param name="cancellationToken">A token to cancel the validation operation.</param>
    /// <returns>A task that represents the asynchronous validation operation. The task result contains the validation result with any errors found.</returns>
    public static Task<NuGetPackageValidationResult> ValidateAsync(FullPath packagePath, CancellationToken cancellationToken = default)
    {
        return ValidateAsync(packagePath, NuGetPackageValidationOptions.Default, cancellationToken);
    }

    /// <summary>Validates a NuGet package using a custom set of validation rules.</summary>
    /// <param name="packagePath">The path to the NuGet package file (.nupkg) to validate.</param>
    /// <param name="rules">The collection of validation rules to apply.</param>
    /// <param name="cancellationToken">A token to cancel the validation operation.</param>
    /// <returns>A task that represents the asynchronous validation operation. The task result contains the validation result with any errors found.</returns>
    public static Task<NuGetPackageValidationResult> ValidateAsync(FullPath packagePath, IEnumerable<NuGetPackageValidationRule> rules, CancellationToken cancellationToken = default)
    {
        var options = new NuGetPackageValidationOptions();
        foreach (var rule in rules)
        {
            options.Rules.Add(rule);
        }

        return ValidateAsync(packagePath, options, cancellationToken);
    }

    /// <summary>Validates a NuGet package using custom validation options.</summary>
    /// <param name="packagePath">The path to the NuGet package file (.nupkg) to validate.</param>
    /// <param name="options">The validation options that specify which rules to apply and other configuration settings.</param>
    /// <param name="cancellationToken">A token to cancel the validation operation.</param>
    /// <returns>A task that represents the asynchronous validation operation. The task result contains the validation result with any errors found.</returns>
    public static async Task<NuGetPackageValidationResult> ValidateAsync(FullPath packagePath, NuGetPackageValidationOptions options, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(packagePath))
        {
            return new NuGetPackageValidationResult(
            [
                new (ErrorCodes.FileNotFound, $"NuGet package '{packagePath}' not found", helpText: null),
            ]);
        }

        using var context = new NuGetPackageValidationContext(packagePath, options, cancellationToken);
        foreach (var rule in options.Rules)
        {
            await rule.ExecuteAsync(context).ConfigureAwait(false);
        }

        return new NuGetPackageValidationResult(context.Errors);
    }
}
