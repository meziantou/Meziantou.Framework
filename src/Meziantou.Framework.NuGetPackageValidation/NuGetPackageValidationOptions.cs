namespace Meziantou.Framework.NuGetPackageValidation;

public sealed class NuGetPackageValidationOptions
{
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

    public ICollection<NuGetPackageValidationRule> Rules { get; } = new HashSet<NuGetPackageValidationRule>();

    public ICollection<int> ExcludedRuleIds { get; } = new HashSet<int>();
}
