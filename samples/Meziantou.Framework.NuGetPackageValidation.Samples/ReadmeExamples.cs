namespace Meziantou.Framework.NuGetPackageValidation.Samples;

internal static class ReadmeExamples
{
    public static async Task BasicValidationAsync()
    {
        #region NuGetPackageValidator_BasicValidation
        // Validate a package with default rules
        var result = await NuGetPackageValidator.ValidateAsync(FullPath.FromPath("MyPackage.1.0.0.nupkg"));

        if (result.IsValid)
        {
            Console.WriteLine("Package is valid!");
        }
        else
        {
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"{error.ErrorCode}: {error.Message}");
                if (error.HelpText is not null)
                {
                    Console.WriteLine($"  Help: {error.HelpText}");
                }
            }
        }
        #endregion
    }

    public static async Task CustomOptionsAsync()
    {
        #region NuGetPackageValidator_CustomOptions
        var options = new NuGetPackageValidationOptions();

        // Add default rules
        foreach (var rule in NuGetPackageValidationRules.Default)
        {
            options.Rules.Add(rule);
        }

        // Exclude specific error codes
        options.ExcludedRuleIds.Add(32); // IconNotSet

        // Configure symbol servers
        options.SymbolServers.Clear();
        options.SymbolServers.Add("https://msdl.microsoft.com/download/symbols/");
        options.SymbolServers.Add("https://symbols.nuget.org/download/symbols/");

        // Configure HTTP requests (e.g., add authentication)
        options.ConfigureRequest = request =>
        {
            request.Headers.Add("X-Custom-Header", "value");
        };

        var result = await NuGetPackageValidator.ValidateAsync(FullPath.FromPath("MyPackage.1.0.0.nupkg"), options);
        #endregion

        Console.WriteLine(result.IsValid);
    }
}
