namespace Meziantou.Framework.SnapshotTesting;

public static class SnapshotSettingsCloneExtensions
{
    extension(SnapshotSettings settings)
    {
        public SnapshotSettings Clone() => settings with { };
    }
}
