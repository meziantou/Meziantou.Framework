using Meziantou.Framework.InlineSnapshotTesting.MergeTools;
using Xunit;

namespace Meziantou.Framework.InlineSnapshotTesting.Tests;
public sealed class MergeToolTests
{
    [Fact]
    public void ValidateEnumMembers()
    {
        var diffToolNames = Enum.GetNames<DiffEngine.DiffTool>();
        var inlineSnapshotPreferredDiffToolNames =
            typeof(MergeTool)
            .GetProperties(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)
            .Where(p => p.CanRead && typeof(MergeTool).IsAssignableFrom(p.PropertyType))
            .Select(p => p.Name);
        Assert.Empty(diffToolNames.Except(inlineSnapshotPreferredDiffToolNames, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("ping", "ping", "")]
    [InlineData("ping ", "ping", "")]
    [InlineData("ping a b", "ping", "a b")]
    [InlineData("\"ping\"", "ping", "")]
    [InlineData("\"ping\" ", "ping", "")]
    [InlineData("\"ping\" a b", "ping", "a b")]
    public void GitTool_ParseCommand(string value, string command, string arguments)
    {
        Assert.Equal((command, arguments), GitTool.ParseCommandFromConfiguration(value));
    }
}
