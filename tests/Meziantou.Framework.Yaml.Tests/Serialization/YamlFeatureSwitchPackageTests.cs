using System.IO.Compression;
using System.Reflection;

namespace Meziantou.Framework.Yaml.Tests.Serialization;

public sealed class YamlFeatureSwitchPackageTests(YamlPackageFixture yamlPackageFixture) : IClassFixture<YamlPackageFixture>
{
    private static ZipArchiveEntry AssertEntry(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        Assert.NotNull(entry);

        return entry;
    }

    private static string ReadEntryText(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);

        return memoryStream.ToArray();
    }

    private static string GetRuntimeAssemblyEntryName()
    {
#if NET11_0_OR_GREATER
        return "lib/net11.0/Meziantou.Framework.Yaml.dll";
#else
        return "lib/net10.0/Meziantou.Framework.Yaml.dll";
#endif
    }

    [Fact]
    public void Package_ContainsFeatureSwitchFiles()
    {
        using var package = ZipFile.OpenRead(yamlPackageFixture.PackagePath);
        var targetsText = ReadEntryText(AssertEntry(package, "buildTransitive/Meziantou.Framework.Yaml.targets"));
        Assert.Contains("RuntimeHostConfigurationOption Include=\"Meziantou.Framework.Yaml.YamlSerializer.IsReflectionEnabledByDefault\"", targetsText);
        Assert.Contains("<MeziantouFrameworkYamlIsReflectionEnabledByDefault", targetsText);
        Assert.Contains("Trim=\"true\"", targetsText);

        AssertEntry(package, "analyzers/dotnet/cs/Meziantou.Framework.Yaml.SourceGenerator.dll");
        AssertEntry(package, "lib/net10.0/Meziantou.Framework.Yaml.dll");
        AssertEntry(package, "lib/net11.0/Meziantou.Framework.Yaml.dll");
        var runtimeAssemblyEntry = AssertEntry(package, GetRuntimeAssemblyEntryName());
        var runtimeAssembly = Assembly.Load(ReadEntryBytes(runtimeAssemblyEntry));
        Assert.Contains("ILLink.Substitutions.xml", runtimeAssembly.GetManifestResourceNames());
    }
}
