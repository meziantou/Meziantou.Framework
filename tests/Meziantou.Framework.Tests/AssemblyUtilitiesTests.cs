using System.Reflection;

namespace Meziantou.Framework.Tests;

public sealed class AssemblyUtilitiesTests
{
    [Fact]
    public void GetLinkerTimestampUtc_ReturnsNullForADeterministicBuild()
    {
        var location = typeof(AssemblyUtilities).Assembly.Location;
        Assert.NotEmpty(location);

        // The SDK builds deterministically by default, so TimeDateStamp holds a hash and not a date
        Assert.Null(AssemblyUtilities.GetLinkerTimestampUtc(location));
    }

    [Fact]
    public void GetLinkerTimestampUtc_ReturnsNullForAMissingFile()
    {
        Assert.Null(AssemblyUtilities.GetLinkerTimestampUtc(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dll")));
    }

    [Fact]
    public void GetLinkerTimestampUtc_ReturnsNullForAFileThatIsNotAnAssembly()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "not a PE file");
            Assert.Null(AssemblyUtilities.GetLinkerTimestampUtc(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetLinkerTimestampUtc_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => AssemblyUtilities.GetLinkerTimestampUtc(filePath: null!));
    }

    [Fact]
    public void GetInformationalVersion_ReturnsTheAttributeValue()
    {
        var assembly = typeof(AssemblyUtilities).Assembly;
        var expected = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        Assert.Equal(expected, assembly.GetInformationalVersion());
    }
}
