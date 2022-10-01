using System.Collections.Concurrent;
using NuGet.Packaging;

namespace Meziantou.Framework.NuGetPackageValidation;

public sealed class NuGetPackageValidationContext : IDisposable
{
    private readonly ConcurrentBag<NuGetPackageValidationError> _errors = new();
    private readonly NuGetPackageValidationOptions _options;

    internal NuGetPackageValidationContext(FullPath file, NuGetPackageValidationOptions options, CancellationToken cancellationToken)
    {
        PackagePath = file;
        _options = options;
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

    public bool IsRuleExcluded(int ruleId)
    {
        return _options.ExcludedRuleIds.Contains(ruleId);
    }

    public void Dispose()
    {
        Package.Dispose();
        SymbolPackage?.Dispose();
    }

    public void ReportError(int errorCode, string message, string? fileName = null)
    {
        if (_options.ExcludedRuleIds.Contains(errorCode))
            return;

        _errors.Add(new NuGetPackageValidationError(errorCode, message, fileName));
    }
}
