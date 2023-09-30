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
        if (!Version.TryParse(newValue, out _))
        {
            // Version may be a semantic version which is not valid as an assembly version
            // Remove the prerelease and metadata parts
            var indexOfAny = newValue.IndexOfAny(['+', '-']);
            if (indexOfAny > 0)
            {
                newValue = newValue[..indexOfAny];
            }
        }

        // An assembly version must have 4 components
        var components = newValue.Count(c => c == '.') + 1;
        if (components < 4)
        {
            for (var i = 0; i < 4 - components; i++)
            {
                newValue += ".0";
            }
        }

        return base.UpdateCoreAsync(oldValue, newValue, cancellationToken);
    }
}
