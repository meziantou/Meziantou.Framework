using System.Runtime.InteropServices;

namespace Meziantou.Framework.InlineSnapshotTesting.MergeTools;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct MergeToolLaunchFailure(MergeTool Tool, Exception Exception);
