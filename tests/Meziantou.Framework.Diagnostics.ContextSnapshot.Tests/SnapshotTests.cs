#pragma warning disable CA1869

using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using Xunit.Abstractions;

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
    public void SpecialFolderShouldContainsAllValues()
    {
        var snapshot = new SpecialFolderSnapshot();

        foreach (var folder in Enum.GetNames<Environment.SpecialFolder>())
        {
            var expectedValue = Environment.GetFolderPath(Enum.Parse<Environment.SpecialFolder>(folder, ignoreCase: false));
            var actualValue = typeof(SpecialFolderSnapshot).GetProperty(folder).GetValue(snapshot);

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
}