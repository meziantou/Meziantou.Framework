namespace Meziantou.Framework.NuGetPackageValidation.Rules;

internal sealed class IconMustBeSetValidationRule : NuGetPackageValidationRule
{
    public override async Task ExecuteAsync(NuGetPackageValidationContext context)
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
        else
        {
            var extension = Path.GetExtension(icon);
            if (!string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".jepg", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
            {
                context.ReportError(ErrorCodes.IconFileFormatNotSupported, "Icon file must be PNG or JPG", fileName: icon);
            }

            var stream = await context.Package.GetStreamAsync(icon, context.CancellationToken).ConfigureAwait(false);
            try
            {
                const long MaxAllowedSize = 1000000;// 1MB https://docs.microsoft.com/en-us/nuget/reference/nuspec?WT.mc_id=DT-MVP-5003978#icon
                var buffer = new byte[128];
                var length = 0L;
                while (await stream.ReadAsync(buffer, context.CancellationToken).ConfigureAwait(false) is int read && read > 0)
                {
                    length += read;
                    if (length > MaxAllowedSize)
                    {
                        context.ReportError(ErrorCodes.IconFileTooLarge, "Icon file is too large, image file size is limited to 1 MB", fileName: icon);
                        break;
                    }
                }
            }
            finally
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}