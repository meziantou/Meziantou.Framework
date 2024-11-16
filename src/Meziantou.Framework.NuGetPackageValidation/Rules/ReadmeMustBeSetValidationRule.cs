namespace Meziantou.Framework.NuGetPackageValidation.Rules;

internal sealed class ReadmeMustBeSetValidationRule : NuGetPackageValidationRule
{
    public override Task ExecuteAsync(NuGetPackageValidationContext context)
    {
        var value = context.Package.NuspecReader.GetReadme();
        if (string.IsNullOrWhiteSpace(value))
        {
            context.ReportError(ErrorCodes.ReadmeNotSet, "Readme is not set");
        }
        else if (!PackageFileExists(context.Package, value))
        {
            context.ReportError(ErrorCodes.ReadmeFileNotFound, "Readme file not found", fileName: value);

        }

        return Task.CompletedTask;
    }
}
