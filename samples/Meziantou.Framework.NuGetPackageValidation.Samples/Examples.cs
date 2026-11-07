namespace Meziantou.Framework.NuGetPackageValidation.Samples;

internal static class Examples
{
    public static async Task ValidatePackage()
    {
        #region NuGetPackageValidator_Example
        // Validate a package with default rules
        var packagePath = FullPath.FromPath("MyPackage.1.0.0.nupkg");
        var result = await NuGetPackageValidator.ValidateAsync(packagePath);
        if (!result.IsValid)
        {
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"{error.ErrorCode}: {error.Message}");
            }
        }

        // Validate with specific rules
        var options = new NuGetPackageValidationOptions();
        options.Rules.Add(NuGetPackageValidationRules.LicenseMustBeSet);
        options.Rules.Add(NuGetPackageValidationRules.AuthorMustBeSet);
        var customResult = await NuGetPackageValidator.ValidateAsync(packagePath, options);
        #endregion

        Console.WriteLine(customResult.IsValid);
    }
}
