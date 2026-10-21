using System.Runtime.CompilerServices;

namespace Meziantou.Framework.InlineSnapshotTesting.Samples;

#region InlineSnapshot_ModuleInitializer
internal static class AssemblyInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        InlineSnapshotSettings.Default = InlineSnapshotSettings.Default with
        {
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.MergeTool,
            MergeTools = [MergeTool.VisualStudioCode],
        };
    }
}
#endregion
