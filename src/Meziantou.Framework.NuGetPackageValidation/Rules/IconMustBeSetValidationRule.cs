namespace Meziantou.Framework.NuGetPackageValidation.Rules;

internal sealed class IconMustBeSetValidationRule : NuGetPackageValidationRule
{
    public override Task ExecuteAsync(NuGetPackageValidationContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.Package.NuspecReader.GetIconUrl()))
        {
            context.ReportError(ErrorCodes.UseDeprecatedIconUrl, "The nuspec file use the deprecated iconUrl metadata");
        }

        var icon = context.Package.NuspecReader.GetIcon();
        if (string.IsNullOrEmpty(icon))
        {
            context.ReportError(ErrorCodes.IconNotSet, "The package does not have an icon");
        }
        else if (!PackageFileExists(context.Package, icon))
        {
            context.ReportError(ErrorCodes.IconNotFound, "Icon file not found", fileName: icon);
        }

        return Task.CompletedTask;
    }
}
