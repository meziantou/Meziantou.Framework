using System.Xml.Linq;

namespace Meziantou.Framework.DependencyScanning;

internal sealed class AssemblyVersionXmlLocation : XmlLocation
{
    public AssemblyVersionXmlLocation(string filePath, XElement element, string attributeName, int column, int length)
        : base(filePath, element, attributeName, column, length)
    {
    }

    protected internal override Task UpdateAsync(Stream stream, string newVersion, CancellationToken cancellationToken)
    {
        if (!Version.TryParse(newVersion, out _))
        {
            // Version may be a semantic version which is not valid as an assembly version
            // Remove the prerelease and metadata parts
            var indexOfAny = newVersion.IndexOfAny(new[] { '+', '-' });
            if (indexOfAny > 0)
            {
                newVersion = newVersion[..indexOfAny];
            }
        }

        // An assembly version must have 4 components
        var components = newVersion.Count(c => c == '.') + 1;
        if (components < 4)
        {
            for (var i = 0; i < 4 - components; i++)
            {
                newVersion += ".0";
            }
        }

        return base.UpdateAsync(stream, newVersion, cancellationToken);
    }
}
