namespace Meziantou.Framework.NuGetPackageValidation.Rules;

internal sealed class DescriptionMustBeSetValidationRule : NuGetPackageValidationRule
{
    public override Task ExecuteAsync(NuGetPackageValidationContext context)
    {
        if (!string.IsNullOrEmpty(context.Package.NuspecReader.GetSummary()))
        {
            context.ReportError(ErrorCodes.UseDeprecatedSummary, "The nuspec file use the deprecated summary metadata");
        }

        var description = context.Package.NuspecReader.GetDescription();
        if (string.IsNullOrEmpty(description))
        {
            context.ReportError(ErrorCodes.DescriptionNotSet, "The package description is not set");
        }
        else if (string.Equals(description, "Package Description", StringComparison.OrdinalIgnoreCase))
        {
            context.ReportError(ErrorCodes.PackageHasDefaultDescription, "The package description is not set");
        }
        else if (description.Length > 4000)
        {
            context.ReportError(ErrorCodes.PackageDescriptionIsTooLong, "The package description is limited to 4000 characters");
        }

        return Task.CompletedTask;
    }
}
