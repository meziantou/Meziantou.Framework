namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Selects which of the resources created by this library a cleanup removes.</summary>
public enum ContainerCleanupScope
{
    /// <summary>Only the resources whose creating process is no longer running on this machine. A resource created on another machine, or by a process that is still alive, is left alone.</summary>
    Orphaned,

    /// <summary>Every resource created by this library, whatever created it, except the ones the current process created.</summary>
    All,
}
