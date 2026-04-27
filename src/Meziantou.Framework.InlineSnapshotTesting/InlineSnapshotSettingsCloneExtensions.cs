namespace Meziantou.Framework.InlineSnapshotTesting;

public static class InlineSnapshotSettingsCloneExtensions
{
    extension(InlineSnapshotSettings settings)
    {
        public InlineSnapshotSettings Clone() => settings with { };
    }
}
