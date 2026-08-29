#pragma warning disable CA1869

using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;
using Windows.Win32.System.SystemInformation;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot.Tests;

public sealed class SnapshotTests(ITestOutputHelper testOutputHelper)
{
    [Fact]
    public void IsJsonSerializable()
    {
        var builder = new ContextSnapshotBuilder();
        builder.AddDefault();
        var snapshot = builder.BuildSnapshot();

        testOutputHelper.WriteLine(JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters =
            {
                new JsonStringEnumConverter(),
            },
        }));
    }

    [Fact]
    public void HasAppContextData()
    {
        var builder = new ContextSnapshotBuilder();
        builder.AddAppContextData();
        var snapshot = builder.BuildSnapshot();
        Assert.NotEmpty(snapshot);
    }

    [Fact]
    public void PathEnvironmentVariableIsSplitOnThePlatformSeparator()
    {
        var builder = new ContextSnapshotBuilder();
        builder.AddEnvironmentVariables(EnvironmentVariableTarget.Process);
        var snapshot = builder.BuildSnapshot();

        var variables = Assert.IsType<ImmutableSortedDictionary<string, object>>(snapshot["EnvironmentVariables.Process"]);
        var expected = Environment.GetEnvironmentVariable("PATH")!.Split(Path.PathSeparator);

        // Windows names the variable "Path" and the snapshot dictionary compares keys ordinally.
        var pathKey = variables.Keys.Single(key => string.Equals(key, "PATH", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(expected, Assert.IsType<ImmutableArray<string>>(variables[pathKey]));
    }

    [Fact]
    public void SecretShapedEnvironmentVariablesAreRedactedByDefault()
    {
        Environment.SetEnvironmentVariable("CONTEXTSNAPSHOT_TEST_API_TOKEN", "super-secret");
        Environment.SetEnvironmentVariable("CONTEXTSNAPSHOT_TEST_PLAIN", "visible");
        try
        {
            var builder = new ContextSnapshotBuilder();
            builder.AddEnvironmentVariables(EnvironmentVariableTarget.Process);
            var variables = Assert.IsType<ImmutableSortedDictionary<string, object>>(builder.BuildSnapshot()["EnvironmentVariables.Process"]);

            Assert.Equal(ContextSnapshotBuilder.RedactedValue, variables["CONTEXTSNAPSHOT_TEST_API_TOKEN"]);
            Assert.Equal("visible", variables["CONTEXTSNAPSHOT_TEST_PLAIN"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CONTEXTSNAPSHOT_TEST_API_TOKEN", value: null);
            Environment.SetEnvironmentVariable("CONTEXTSNAPSHOT_TEST_PLAIN", value: null);
        }
    }

    [Fact]
    public void EnvironmentVariableRedactionCanBeOverridden()
    {
        Environment.SetEnvironmentVariable("CONTEXTSNAPSHOT_TEST_API_TOKEN", "super-secret");
        try
        {
            var builder = new ContextSnapshotBuilder();
            builder.AddEnvironmentVariables(EnvironmentVariableTarget.Process, _ => false);
            var variables = Assert.IsType<ImmutableSortedDictionary<string, object>>(builder.BuildSnapshot()["EnvironmentVariables.Process"]);

            Assert.Equal("super-secret", variables["CONTEXTSNAPSHOT_TEST_API_TOKEN"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CONTEXTSNAPSHOT_TEST_API_TOKEN", value: null);
        }
    }

    [Fact]
    public void SpecialFolderShouldContainsAllValues()
    {
        var snapshot = new SpecialFolderSnapshot();

        foreach (var folder in Enum.GetNames<Environment.SpecialFolder>())
        {
            var expectedValue = Environment.GetFolderPath(Enum.Parse<Environment.SpecialFolder>(folder, ignoreCase: false));
            var property = typeof(SpecialFolderSnapshot).GetProperty(folder);
            Assert.NotNull(property);
            var actualValue = property.GetValue(snapshot);

            Assert.Equal(expectedValue, actualValue);
        }
    }

    [Fact]
    public void CpuSnapshotTest()
    {
        var snapshot = CpuSnapshot.Get();
        Assert.NotEqual(0, snapshot.LogicalCoreCount);
        Assert.NotEqual(0, snapshot.PhysicalCoreCount);
        Assert.NotEqual(0, snapshot.MaxFrequency);
    }

    [Fact]
    public void ProcCpuInfoParserReadsAppendedFrequencies()
    {
        // /proc/cpuinfo blocks are separated by a blank line and the file ends with one. The frequencies
        // are appended as an extra section, which must not be mistaken for another logical core.
        var content = "processor\t: 0\nphysical id\t: 0\ncpu cores\t: 2\nmodel name\t: Intel(R) Core(TM) i7 CPU @ 3.20GHz\n\n"
                    + "processor\t: 1\nphysical id\t: 0\ncpu cores\t: 2\nmodel name\t: Intel(R) Core(TM) i7 CPU @ 3.20GHz\n\n"
                    + "\nmin freq\t:800\nmax freq\t:3200";

        var cpuInfo = ProcCpuInfoParser.ParseOutput(content);

        Assert.Equal("Intel(R) Core(TM) i7 CPU @ 3.20GHz", cpuInfo.ProcessorName);
        Assert.Equal(1, cpuInfo.PhysicalProcessorCount);
        Assert.Equal(2, cpuInfo.PhysicalCoreCount);
        Assert.Equal(2, cpuInfo.LogicalCoreCount);
        Assert.Equal(Frequency.FromMHz(800), cpuInfo.MinFrequency);
        Assert.Equal(Frequency.FromMHz(3200), cpuInfo.MaxFrequency);
    }

    [Fact]
    public void ProcCpuInfoParserReadsFrequenciesConvertedFromKiloHertz()
    {
        // cpuinfo_min_freq / cpuinfo_max_freq are in kHz; the provider converts them to the MHz form the parser expects.
        var minFrequency = new Frequency(800000, FrequencyUnit.KHz);
        var maxFrequency = new Frequency(3200000, FrequencyUnit.KHz);
        var content = $"model name\t: CPU\n\n\nmin freq\t:{minFrequency.ToMHz()}\nmax freq\t:{maxFrequency.ToMHz()}";

        var cpuInfo = ProcCpuInfoParser.ParseOutput(content);

        Assert.Equal(Frequency.FromMHz(800), cpuInfo.MinFrequency);
        Assert.Equal(Frequency.FromMHz(3200), cpuInfo.MaxFrequency);
    }

    [Fact]
    public unsafe void CountProcessorsWalksVariableSizedEntries()
    {
        var buffer = new List<byte>();
        AddProcessorEntry(buffer, LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorPackage, groupMask: 0b1111);

        // An entry of another kind, smaller than SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX, must be skipped using its Size
        AddOpaqueEntry(buffer, LOGICAL_PROCESSOR_RELATIONSHIP.RelationCache, size: 16);

        AddProcessorEntry(buffer, LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore, groupMask: 0b0011);
        AddProcessorEntry(buffer, LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore, groupMask: 0b1100);

        var bytes = buffer.ToArray();
        fixed (byte* ptr = bytes)
        {
            WindowsCpuInfoProvider.CountProcessors(ptr, (uint)bytes.Length, out var physicalProcessorCount, out var physicalCoreCount, out var logicalCoreCount);
            Assert.Equal(1, physicalProcessorCount);
            Assert.Equal(2, physicalCoreCount);
            Assert.Equal(4, logicalCoreCount);
        }

        static void AddProcessorEntry(List<byte> buffer, LOGICAL_PROCESSOR_RELATIONSHIP relationship, nuint groupMask)
        {
            var entry = default(SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX);
            entry.Relationship = relationship;
            entry.Size = (uint)sizeof(SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX);
            entry.Anonymous.Processor.GroupCount = 1;
            entry.Anonymous.Processor.GroupMask[0].Mask = groupMask;
            buffer.AddRange(MemoryMarshal.AsBytes(new ReadOnlySpan<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>(in entry)));
        }

        static void AddOpaqueEntry(List<byte> buffer, LOGICAL_PROCESSOR_RELATIONSHIP relationship, uint size)
        {
            var entry = new byte[size];
            BitConverter.TryWriteBytes(entry, (int)relationship);
            BitConverter.TryWriteBytes(entry.AsSpan(sizeof(int)), size);
            buffer.AddRange(entry);
        }
    }
}
