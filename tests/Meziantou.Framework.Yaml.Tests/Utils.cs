namespace Meziantou.Framework.Yaml.Tests;

public static class Utils
{
    public static Stream GetResourceStream(string fileName)
    {
        var assembly = typeof(Utils).Assembly;
        var stream = assembly.GetManifestResourceStream("Meziantou.Framework.Yaml.Tests.files." + fileName);
        if (stream is null)
        {
            var resourceNames = assembly.GetManifestResourceNames().Where(n => n.StartsWith("Meziantou.Framework.Yaml.Tests.files.", StringComparison.Ordinal)).ToArray();
            throw new InvalidOperationException($"Resource '{fileName}' not found. Available resources: {string.Join('\n', resourceNames)}");
        }

        return stream;
    }
}