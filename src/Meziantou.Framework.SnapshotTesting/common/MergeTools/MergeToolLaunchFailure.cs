using System.Runtime.InteropServices;

#if MEZIANTOU_INLINE_SNAPSHOT_TESTING
namespace Meziantou.Framework.InlineSnapshotTesting.MergeTools;
#else
namespace Meziantou.Framework.SnapshotTesting.MergeTools;
#endif

[StructLayout(LayoutKind.Auto)]
internal readonly record struct MergeToolLaunchFailure(MergeTool Tool, Exception Exception);
