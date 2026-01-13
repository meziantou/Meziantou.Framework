namespace Meziantou.Framework.InlineSnapshotTesting.MergeTools;

internal sealed class MergeToolFromEnvironment : MergeTool
{
    public override MergeToolResult? Start(string currentFilePath, string newFilePath)
    {
        var variable = Environment.GetEnvironmentVariable("DiffEngine_Tool");
        if (string.IsNullOrEmpty(variable))
            return null;

        var property = typeof(MergeTool).GetProperty(variable, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (property is null)
            return null;

        if (!typeof(MergeTool).IsAssignableFrom(property.PropertyType))
            return null;

        var tool = (MergeTool?)property.GetValue(null);
        if (tool is null)
            return null;

        return tool.Start(currentFilePath, newFilePath);
    }
}
