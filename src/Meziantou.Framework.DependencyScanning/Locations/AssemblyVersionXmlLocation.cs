using System.Xml.Linq;

namespace Meziantou.Framework.DependencyScanning;

/// <summary>The location of the <c>Version=</c> part of an assembly name, such as the Include attribute of a <c>Reference</c> item.</summary>
internal sealed class AssemblyVersionXmlLocation : XmlLocation
{
    private const int MaxComponentValue = ushort.MaxValue - 1;

    public AssemblyVersionXmlLocation(IFileSystem fileSystem, string filePath, XElement element, XAttribute attribute, int column, int length)
        : base(fileSystem, filePath, element, attribute, column, length)
    {
    }

    protected internal override Task UpdateCoreAsync(string? oldValue, string newValue, CancellationToken cancellationToken)
    {
        var normalizedValue = NormalizeAssemblyVersion(newValue);
        return base.UpdateCoreAsync(oldValue, normalizedValue, cancellationToken);
    }

    /// <summary>
    /// Converts a version, such as a NuGet version, to an assembly version: a leading 'v' and the prerelease and build metadata parts are removed,
    /// and missing components are added. An assembly version has at most 4 components, each between 0 and 65534.
    /// </summary>
    internal static string NormalizeAssemblyVersion(string value)
    {
        var version = value.AsSpan().Trim();
        if (version is ['v' or 'V', ..])
        {
            version = version[1..];
        }

        // Version may be a semantic version which is not valid as an assembly version: remove the prerelease and metadata parts
        var suffixIndex = version.IndexOfAny('-', '+');
        if (suffixIndex >= 0)
        {
            version = version[..suffixIndex];
        }

        var components = new List<int>(capacity: 4);
        foreach (var range in version.Split('.'))
        {
            var component = version[range];
            if (components.Count == 4 || component.IsEmpty || component.Length > 5 || !IsAsciiDigits(component))
                throw CreateInvalidVersionException(value);

            var componentValue = int.Parse(component, NumberStyles.None, CultureInfo.InvariantCulture);
            if (componentValue > MaxComponentValue)
                throw CreateInvalidVersionException(value);

            components.Add(componentValue);
        }

        // An assembly version must have 4 components
        while (components.Count < 4)
        {
            components.Add(0);
        }

        return string.Join('.', components.Select(component => component.ToString(CultureInfo.InvariantCulture)));
    }

    private static bool IsAsciiDigits(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }

        return true;
    }

    private static DependencyScannerException CreateInvalidVersionException(string value)
    {
        return new DependencyScannerException($"'{value}' is not a valid assembly version. An assembly version has at most 4 numeric components, each between 0 and {MaxComponentValue.ToString(CultureInfo.InvariantCulture)}.");
    }
}
