using System.Xml.Linq;

namespace Meziantou.Framework.DependencyScanning;

internal sealed class AssemblyVersionXmlLocation : XmlLocation
{
    public AssemblyVersionXmlLocation(IFileSystem fileSystem, string filePath, XElement element, XAttribute attribute, int column, int length)
        : base(fileSystem, filePath, element, attribute, column, length)
    {
    }

    protected internal override Task UpdateCoreAsync(string? oldValue, string newValue, CancellationToken cancellationToken)
    {
        var normalizedValue = NormalizeAssemblyVersion(newValue);
        return base.UpdateCoreAsync(oldValue, normalizedValue, cancellationToken);
    }

    private static string NormalizeAssemblyVersion(string value)
    {
        if (!Version.TryParse(value, out _))
        {
            // Version may be a semantic version which is not valid as an assembly version
            // Remove the prerelease and metadata parts
            var indexOfAny = value.IndexOfAny(['+', '-']);
            if (indexOfAny > 0)
            {
                value = value[..indexOfAny];
            }
        }

        // An assembly version must have 4 components
        var components = value.Count(c => c == '.') + 1;
        if (components < 4)
        {
            for (var i = 0; i < 4 - components; i++)
            {
                value += ".0";
            }
        }

        return value;
    }
}
