namespace Meziantou.Framework.NuGetPackageValidation.Rules;

internal sealed class IconMustBeSetValidationRule : NuGetPackageValidationRule
{
    public override async Task ExecuteAsync(NuGetPackageValidationContext context)
    {
        // https://learn.microsoft.com/en-us/nuget/reference/nuspec?WT.mc_id=DT-MVP-5003978#icon
        // You can specify both icon and iconUrl to maintain backward compatibility with sources that do not support icon.
        var icon = context.Package.NuspecReader.GetIcon();
        if (string.IsNullOrEmpty(icon))
        {
            if (string.IsNullOrWhiteSpace(context.Package.NuspecReader.GetIconUrl()))
            {
                context.ReportError(ErrorCodes.IconNotSet, "The package does not have an icon");
            }
            else
            {
                context.ReportError(ErrorCodes.UseDeprecatedIconUrl, "The nuspec file use the deprecated iconUrl metadata");
            }
        }
        else if (!PackageFileExists(context.Package, icon, out var realPath))
        {
            context.ReportError(ErrorCodes.IconNotFound, "Icon file not found", fileName: icon);
        }
        else
        {
            var extension = Path.GetExtension(icon);
            if (extension is ".png")
            {
                await ValidateMagicNumber(context, realPath, [0x89, 0x50, 0x4E, 0x47], extension, "PNG").ConfigureAwait(false);
            }
            else if (extension is ".jpg" or ".jpeg")
            {
                await ValidateMagicNumber(context, realPath, [0xFF, 0xD8, 0xFF, 0xE0], extension, "JPEG").ConfigureAwait(false);
            }
            else
            {
                context.ReportError(ErrorCodes.IconFileFormatNotSupported, "Icon file must be PNG or JPG", fileName: icon);
            }

            await ValidateFileSize(context, realPath).ConfigureAwait(false);
        }
    }

    private static async Task ValidateMagicNumber(NuGetPackageValidationContext context, string icon, byte[] expectedMagicNumber, string extension, string formatName)
    {
        var stream = await context.Package.GetStreamAsync(icon, context.CancellationToken).ConfigureAwait(false);
        try
        {
            if (!await HasMagicNumberAsync(stream, expectedMagicNumber, context.CancellationToken).ConfigureAwait(false))
            {
                context.ReportError(ErrorCodes.IconFileInvalidExtension, $"Icon file has extension '{extension}' but the file is not a {formatName}", fileName: icon);
            }
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task ValidateFileSize(NuGetPackageValidationContext context, string icon)
    {
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

    private static async Task<bool> HasMagicNumberAsync(Stream stream, byte[] expectedMagicNumber, CancellationToken cancellationToken)
    {
        var buffer = new byte[expectedMagicNumber.Length];
        var read = await TryReadAllAsync(stream, buffer, cancellationToken).ConfigureAwait(false);
        if (read < expectedMagicNumber.Length)
            return false;

        return buffer.SequenceEqual(expectedMagicNumber);
    }

    private static async Task<int> TryReadAllAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var totalRead = 0;
        while (!buffer.IsEmpty)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return totalRead;

            totalRead += read;
            buffer = buffer[read..];
        }

        return totalRead;
    }
}
