using System.Runtime.InteropServices;

#if MEZIANTOU_INLINE_SNAPSHOT_TESTING
namespace Meziantou.Framework.InlineSnapshotTesting.Utils;
#else
namespace Meziantou.Framework.SnapshotTesting.Utils;
#endif

[StructLayout(LayoutKind.Auto)]
internal readonly record struct ProcessEntry(int ProcessId, int ParentProcessId);
