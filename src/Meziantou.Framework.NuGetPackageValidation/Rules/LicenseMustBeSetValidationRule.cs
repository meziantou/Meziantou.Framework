using NuGet.Packaging;

namespace Meziantou.Framework.NuGetPackageValidation.Rules;

internal sealed class LicenseMustBeSetValidationRule : NuGetPackageValidationRule
{
    public override Task ExecuteAsync(NuGetPackageValidationContext context)
    {
        var metadata = context.Package.NuspecReader.GetLicenseMetadata();
        if (metadata is not null)
        {
            if (metadata.Type == LicenseType.Expression)
                return Task.CompletedTask;

            if (metadata.Type == LicenseType.File && !PackageFileExists(context.Package, metadata.License))
            {
                context.ReportError(ErrorCodes.LicenseFileNotFound, "License file not found", fileName: metadata.License);
            }
        }
        else if (!string.IsNullOrWhiteSpace(context.Package.NuspecReader.GetLicenseUrl()))
        {
            context.ReportError(ErrorCodes.UseDeprecatedLicenseUrl, "The nuspec file use the deprecated licenseUrl metadata");
        }
        else
        {
            context.ReportError(ErrorCodes.LicenseNotSet, "License if not set");
        }

        return Task.CompletedTask;
    }
}
