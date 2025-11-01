using System.Text.RegularExpressions;
using NuGet.Packaging;

namespace Meziantou.Framework.NuGetPackageValidation;

/// <summary>Represents the base class for NuGet package validation rules.</summary>
public abstract partial class NuGetPackageValidationRule
{
    /// <summary>Executes the validation rule against the specified package context.</summary>
    /// <param name="context">The validation context containing the package to validate and methods to report errors.</param>
    /// <returns>A task that represents the asynchronous validation operation.</returns>
    public abstract Task ExecuteAsync(NuGetPackageValidationContext context);

    private protected static bool PackageFileExists(PackageReaderBase package, string path)
    {
        return PackageFileExists(package, path, out _);
    }

    private protected static bool PackageFileExists(PackageReaderBase package, string path, [NotNullWhen(true)] out string? realPath)
    {
        path = path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var files = package.GetFiles();
        foreach (var file in files)
        {
            var normalizedPath = file.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            if (string.Equals(normalizedPath, path, StringComparison.Ordinal))
            {
                realPath = file;
                return true;
            }
        }

        realPath = null;
        return false;
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

    private protected static bool IsSatelliteAssembly(string path)
    {
        return SatelliteAssemblyRegex().IsMatch(path);
    }

    [GeneratedRegex(@"(\\|/)[^\\/]+(\\|/)([^\\/]+).resources.dll$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: -1)]
    private static partial Regex SatelliteAssemblyRegex();
}
