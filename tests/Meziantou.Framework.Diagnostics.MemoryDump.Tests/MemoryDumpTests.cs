using Meziantou.Xunit;

namespace Meziantou.Framework.Diagnostics.Tests;

public sealed class MemoryDumpTests
{
    [Theory, RunIf(TestOperatingSystems.Windows | TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    [InlineData(MemoryDumpType.Normal)]
    [InlineData(MemoryDumpType.WithHeap)]
    [InlineData(MemoryDumpType.Triage)]
    [InlineData(MemoryDumpType.Full)]
    public void Write(MemoryDumpType dumpType)
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        var path = temporaryDirectory / "dump.dmp";

        MemoryDump.Write(path, dumpType);

        AssertIsDump(path);
    }

    [Fact, RunIf(TestOperatingSystems.Windows | TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    public async Task WriteAsync()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var path = temporaryDirectory / "dump.dmp";

        await MemoryDump.WriteAsync(path, MemoryDumpType.Normal, TestContext.Current.CancellationToken);

        AssertIsDump(path);
    }

    [Fact, RunIf(TestOperatingSystems.Windows | TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    public void Write_OverwritesExistingFile()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        var path = temporaryDirectory.CreateTextFile("dump.dmp", "existing content");

        MemoryDump.Write(path, MemoryDumpType.Normal);

        AssertIsDump(path);
    }

    [Fact, RunIf(TestOperatingSystems.Windows | TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    public void Write_FileNameContainsPercent()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        var path = temporaryDirectory / "dump-%p-%%.dmp";

        MemoryDump.Write(path, MemoryDumpType.Normal);

        AssertIsDump(path);
        Assert.Single(Directory.GetFiles(temporaryDirectory.FullPath));
    }

    [Fact]
    public void Write_InvalidDumpType()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MemoryDump.Write("dump.dmp", (MemoryDumpType)42));
    }

    private static void AssertIsDump(string path)
    {
        var header = new byte[4];
        using (var stream = File.OpenRead(path))
        {
            stream.ReadExactly(header);
        }

        byte[] expected = true switch
        {
            _ when OperatingSystem.IsWindows() => "MDMP"u8.ToArray(),
            _ when OperatingSystem.IsLinux() => [0x7F, (byte)'E', (byte)'L', (byte)'F'],
            _ when OperatingSystem.IsMacOS() => [0xCF, 0xFA, 0xED, 0xFE], // MH_MAGIC_64
            _ => throw new PlatformNotSupportedException(),
        };

        Assert.Equal(expected, header);
    }
}
