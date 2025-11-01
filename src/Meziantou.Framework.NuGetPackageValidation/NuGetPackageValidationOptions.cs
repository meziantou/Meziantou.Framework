namespace Meziantou.Framework.NuGetPackageValidation;

/// <summary>Provides configuration options for NuGet package validation.</summary>
public sealed class NuGetPackageValidationOptions
{
    /// <summary>Gets the default validation options with all standard validation rules.</summary>
    public static NuGetPackageValidationOptions Default
    {
        get
        {
            var options = new NuGetPackageValidationOptions();
            foreach (var rule in NuGetPackageValidationRules.Default)
            {
                options.Rules.Add(rule);
            }

            return options;
        }
    }

    /// <summary>Gets the collection of validation rules to apply to the package.</summary>
    public ICollection<NuGetPackageValidationRule> Rules { get; } = new HashSet<NuGetPackageValidationRule>();

    /// <summary>Gets the collection of error codes to exclude from validation. Errors with these codes will not be reported.</summary>
    public ICollection<int> ExcludedRuleIds { get; } = new HashSet<int>();

    /// <summary>Gets the list of symbol server URLs to check for availability of PDB files.</summary>
    public IList<string> SymbolServers { get; } = new List<string>()
    {
        "https://msdl.microsoft.com/download/symbols/",
        "https://symbols.nuget.org/download/symbols/",
    };

    /// <summary>Gets or sets an optional action to configure HTTP requests made during validation, such as adding authentication headers.</summary>
    public Action<HttpRequestMessage>? ConfigureRequest { get; set; }
}
