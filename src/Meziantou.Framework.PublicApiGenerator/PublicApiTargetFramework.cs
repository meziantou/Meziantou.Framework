using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;

namespace Meziantou.Framework.PublicApiGenerator;

internal static class PublicApiTargetFramework
{
    public static string GetTargetFrameworkFromAssembly(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(assemblyPath);

        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
        {
            throw new InvalidOperationException($"The file '{assemblyPath}' does not contain .NET metadata.");
        }

        var frameworkMoniker = GetFrameworkMonikerFromMetadata(peReader.GetMetadataReader());
        if (string.IsNullOrEmpty(frameworkMoniker))
        {
            throw new InvalidOperationException($"Cannot infer target framework from '{assemblyPath}'. Set AssemblySource.TargetFrameworkMoniker.");
        }

        return ConvertFrameworkMonikerToTargetFramework(frameworkMoniker);
    }

    public static string GetTargetFrameworkFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var targetFrameworkAttribute = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
        if (!string.IsNullOrEmpty(targetFrameworkAttribute?.FrameworkName))
        {
            return ConvertFrameworkMonikerToTargetFramework(targetFrameworkAttribute.FrameworkName);
        }

        if (!string.IsNullOrEmpty(assembly.Location))
        {
            return GetTargetFrameworkFromAssembly(assembly.Location);
        }

        throw new InvalidOperationException($"Cannot infer target framework from assembly '{assembly.FullName}'. Set AssemblySource.TargetFrameworkMoniker.");
    }

    public static string ToTargetFramework(string targetFrameworkMoniker)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetFrameworkMoniker);

        if (targetFrameworkMoniker.Contains(',', StringComparison.Ordinal))
        {
            return ConvertFrameworkMonikerToTargetFramework(targetFrameworkMoniker);
        }

        return Normalize(targetFrameworkMoniker);
    }

    public static string ToPreprocessorSymbol(string targetFramework)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetFramework);
        var normalizedTargetFramework = ToTargetFramework(targetFramework);
        if (TryMapNetCoreOrDotNetSymbol(normalizedTargetFramework, out var netCoreOrDotNetSymbol))
        {
            return netCoreOrDotNetSymbol;
        }

        if (TryMapNetStandardSymbol(normalizedTargetFramework, out var netStandardSymbol))
        {
            return netStandardSymbol;
        }

        if (TryMapNetFrameworkSymbol(normalizedTargetFramework, out var netFrameworkSymbol))
        {
            return netFrameworkSymbol;
        }

        throw new ArgumentException($"Cannot convert target framework '{targetFramework}' to a preprocessor symbol.", nameof(targetFramework));
    }

    private static string Normalize(string targetFramework)
    {
        var normalized = targetFramework.Trim().ToLowerInvariant();
        var platformSeparatorIndex = normalized.IndexOf('-', StringComparison.Ordinal);
        if (platformSeparatorIndex >= 0)
        {
            normalized = normalized[..platformSeparatorIndex];
        }

        return normalized;
    }

    private static bool IsTargetFrameworkAttribute(MetadataReader metadataReader, CustomAttribute customAttribute)
    {
        var constructor = customAttribute.Constructor;
        switch (constructor.Kind)
        {
            case HandleKind.MemberReference:
                {
                    var memberReference = metadataReader.GetMemberReference((MemberReferenceHandle)constructor);
                    var parent = memberReference.Parent;
                    if (parent.Kind == HandleKind.TypeReference)
                    {
                        var typeReference = metadataReader.GetTypeReference((TypeReferenceHandle)parent);
                        return string.Equals(metadataReader.GetString(typeReference.Namespace), "System.Runtime.Versioning", StringComparison.Ordinal)
                               && string.Equals(metadataReader.GetString(typeReference.Name), nameof(TargetFrameworkAttribute), StringComparison.Ordinal);
                    }

                    if (parent.Kind == HandleKind.TypeDefinition)
                    {
                        var typeDefinition = metadataReader.GetTypeDefinition((TypeDefinitionHandle)parent);
                        return string.Equals(metadataReader.GetString(typeDefinition.Namespace), "System.Runtime.Versioning", StringComparison.Ordinal)
                               && string.Equals(metadataReader.GetString(typeDefinition.Name), nameof(TargetFrameworkAttribute), StringComparison.Ordinal);
                    }

                    return false;
                }
            case HandleKind.MethodDefinition:
                {
                    var methodDefinition = metadataReader.GetMethodDefinition((MethodDefinitionHandle)constructor);
                    var declaringType = metadataReader.GetTypeDefinition(methodDefinition.GetDeclaringType());
                    return string.Equals(metadataReader.GetString(declaringType.Namespace), "System.Runtime.Versioning", StringComparison.Ordinal)
                           && string.Equals(metadataReader.GetString(declaringType.Name), nameof(TargetFrameworkAttribute), StringComparison.Ordinal);
                }
            default:
                return false;
        }
    }

    private static string? GetFrameworkMonikerFromMetadata(MetadataReader metadataReader)
    {
        var assemblyDefinition = metadataReader.GetAssemblyDefinition();
        foreach (var customAttributeHandle in assemblyDefinition.GetCustomAttributes())
        {
            var customAttribute = metadataReader.GetCustomAttribute(customAttributeHandle);
            if (!IsTargetFrameworkAttribute(metadataReader, customAttribute))
            {
                continue;
            }

            var blobReader = metadataReader.GetBlobReader(customAttribute.Value);
            if (blobReader.ReadUInt16() != 0x0001)
            {
                continue;
            }

            var frameworkMoniker = blobReader.ReadSerializedString();
            if (!string.IsNullOrEmpty(frameworkMoniker))
            {
                return frameworkMoniker;
            }
        }

        return null;
    }

    private static string ConvertFrameworkMonikerToTargetFramework(string frameworkMoniker)
    {
        ArgumentException.ThrowIfNullOrEmpty(frameworkMoniker);

        var frameworkName = new FrameworkName(frameworkMoniker);
        var identifier = frameworkName.Identifier;
        var version = frameworkName.Version;
        if (string.Equals(identifier, ".NETCoreApp", StringComparison.Ordinal))
        {
            if (version.Major >= 5)
            {
                return string.Create(CultureInfo.InvariantCulture, $"net{version.Major}.{GetVersionComponent(version.Minor)}");
            }

            return string.Create(CultureInfo.InvariantCulture, $"netcoreapp{version.Major}.{GetVersionComponent(version.Minor)}");
        }

        if (string.Equals(identifier, ".NETStandard", StringComparison.Ordinal))
        {
            return string.Create(CultureInfo.InvariantCulture, $"netstandard{version.Major}.{GetVersionComponent(version.Minor)}");
        }

        if (string.Equals(identifier, ".NETFramework", StringComparison.Ordinal))
        {
            var patch = version.Build > 0 ? version.Build.ToString(CultureInfo.InvariantCulture) : string.Empty;
            return string.Create(CultureInfo.InvariantCulture, $"net{version.Major}{GetVersionComponent(version.Minor)}{patch}");
        }

        throw new InvalidOperationException($"Unsupported target framework moniker '{frameworkMoniker}'. Set AssemblySource.TargetFrameworkMoniker to a supported value.");
    }

    private static bool TryMapNetCoreOrDotNetSymbol(string targetFramework, out string symbol)
    {
        symbol = string.Empty;
        if (targetFramework.StartsWith("netcoreapp", StringComparison.Ordinal))
        {
            if (!TryParseVersion(targetFramework["netcoreapp".Length..], out var netCoreMajor, out var netCoreMinor))
            {
                return false;
            }

            symbol = FormattableString.Invariant($"NETCOREAPP{netCoreMajor}_{netCoreMinor}");
            return true;
        }

        if (!targetFramework.StartsWith("net", StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryParseVersion(targetFramework["net".Length..], out var netMajor, out var netMinor))
        {
            return false;
        }

        if (netMajor >= 5)
        {
            symbol = FormattableString.Invariant($"NET{netMajor}_{netMinor}");
            return true;
        }

        return false;
    }

    private static bool TryMapNetStandardSymbol(string targetFramework, out string symbol)
    {
        symbol = string.Empty;
        if (!targetFramework.StartsWith("netstandard", StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryParseVersion(targetFramework["netstandard".Length..], out var major, out var minor))
        {
            return false;
        }

        symbol = FormattableString.Invariant($"NETSTANDARD{major}_{minor}");
        return true;
    }

    private static bool TryMapNetFrameworkSymbol(string targetFramework, out string symbol)
    {
        symbol = string.Empty;
        if (!targetFramework.StartsWith("net", StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryParseVersion(targetFramework["net".Length..], out var major, out var minor, out var patch))
        {
            return false;
        }

        if (major >= 5)
        {
            return false;
        }

        symbol = patch > 0
            ? FormattableString.Invariant($"NET{major}{minor}{patch}")
            : FormattableString.Invariant($"NET{major}{minor}");
        return true;
    }

    private static bool TryParseVersion(string value, out int major, out int minor)
    {
        var result = TryParseVersion(value, out major, out minor, out _);
        return result;
    }

    private static bool TryParseVersion(string value, out int major, out int minor, out int patch)
    {
        major = 0;
        minor = 0;
        patch = 0;

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var versionWithoutProfile = value;
        var profileSeparatorIndex = versionWithoutProfile.IndexOf('+', StringComparison.Ordinal);
        if (profileSeparatorIndex >= 0)
        {
            versionWithoutProfile = versionWithoutProfile[..profileSeparatorIndex];
        }

        if (versionWithoutProfile.Contains('.', StringComparison.Ordinal))
        {
            var parts = versionWithoutProfile.Split('.');
            if (parts.Length < 1 || parts.Length > 3)
            {
                return false;
            }

            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out major))
            {
                return false;
            }

            if (parts.Length > 1 && !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minor))
            {
                return false;
            }

            if (parts.Length > 2 && !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out patch))
            {
                return false;
            }

            return true;
        }

        if (!int.TryParse(versionWithoutProfile, NumberStyles.None, CultureInfo.InvariantCulture, out var compactVersion))
        {
            return false;
        }

        if (compactVersion >= 100)
        {
            major = compactVersion / 100;
            minor = (compactVersion / 10) % 10;
            patch = compactVersion % 10;
            return true;
        }

        if (compactVersion >= 10)
        {
            major = compactVersion / 10;
            minor = compactVersion % 10;
            patch = 0;
            return true;
        }

        major = compactVersion;
        minor = 0;
        patch = 0;
        return true;
    }

    private static int GetVersionComponent(int component)
    {
        return component < 0 ? 0 : component;
    }
}
