using System.Collections.Concurrent;
using NuGet.Packaging;

namespace Meziantou.Framework.NuGetPackageValidation;

public sealed class NuGetPackageValidationContext : IDisposable
{
    private readonly ConcurrentBag<NuGetPackageValidationError> _errors = new();

    internal NuGetPackageValidationContext(FullPath file, CancellationToken cancellationToken)
    {
        PackagePath = file;
        CancellationToken = cancellationToken;
        Package = new PackageArchiveReader(file);

        var symbolPackagePath = file.ChangeExtension(".snupkg");
        if (File.Exists(symbolPackagePath))
        {
            SymbolPackage = new PackageArchiveReader(symbolPackagePath);
        }
    }

    public FullPath PackagePath { get; }
    public CancellationToken CancellationToken { get; }
    public PackageReaderBase Package { get; }
    public PackageReaderBase? SymbolPackage { get; }

    internal IReadOnlyCollection<NuGetPackageValidationError> Errors => _errors;

    public void Dispose()
    {
        Package.Dispose();
        SymbolPackage?.Dispose();
    }

    public void ReportError(int errorCode, string message, string? fileName = null)
    {
        _errors.Add(new NuGetPackageValidationError(errorCode, message, fileName));
    }
}
