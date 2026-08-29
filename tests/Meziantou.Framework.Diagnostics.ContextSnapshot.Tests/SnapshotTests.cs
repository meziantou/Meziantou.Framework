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

        Assert.Equal(expected, Assert.IsType<ImmutableArray<string>>(variables["PATH"]));
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
