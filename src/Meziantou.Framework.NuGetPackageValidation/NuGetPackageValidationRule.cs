using NuGet.Packaging;

namespace Meziantou.Framework.NuGetPackageValidation;

public abstract class NuGetPackageValidationRule
{
    public abstract Task ExecuteAsync(NuGetPackageValidationContext context);

    private protected static bool PackageFileExists(PackageReaderBase package, string path)
    {
        try
        {
            using var stream = package.GetStream(path);
            return stream != null;
        }
        catch
        {
            return false;
        }
    }

    private protected static async Task<Stream> CreateSeekableStream(Stream stream, CancellationToken cancellationToken)
    {
        var ms = new MemoryStream();
        try
        {
            await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            ms.Seek(0, SeekOrigin.Begin);
            return ms;
        }
        catch
        {
            await ms.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
