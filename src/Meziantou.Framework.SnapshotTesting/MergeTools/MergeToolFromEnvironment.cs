using System.Reflection;

namespace Meziantou.Framework.SnapshotTesting.MergeTools;

internal sealed class MergeToolFromEnvironment : MergeTool
{
    public override MergeToolResult? Start(string currentFilePath, string newFilePath) => Start(currentFilePath, newFilePath, waitForMerge: false);

    internal override MergeToolResult? Start(string currentFilePath, string newFilePath, bool waitForMerge)
    {
        return GetTool()?.Start(currentFilePath, newFilePath, waitForMerge);
    }

    /// <summary>Resolves the <see cref="MergeTool" /> property named by <c>DiffEngine_Tool</c>, ignoring the case.</summary>
    internal static MergeTool? GetTool()
    {
        var variable = Environment.GetEnvironmentVariable("DiffEngine_Tool")?.Trim();
        if (string.IsNullOrEmpty(variable))
            return null;

        foreach (var property in typeof(MergeTool).GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            if (!string.Equals(property.Name, variable, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!typeof(MergeTool).IsAssignableFrom(property.PropertyType))
                return null;

            // This instance is itself exposed as MergeTool.DiffToolFromEnvironmentVariable, so resolving that name
            // would hand back this very object and recurse until the process died with a StackOverflowException.
            var tool = (MergeTool?)property.GetValue(null);
            if (tool is null or MergeToolFromEnvironment)
                return null;

            return tool;
        }

        return null;
    }

    public override string ToString() => nameof(MergeTool.DiffToolFromEnvironmentVariable);
}
