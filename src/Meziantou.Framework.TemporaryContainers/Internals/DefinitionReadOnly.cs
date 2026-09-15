namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>Guards the definitions a container or a volume was created from. The container reads its definition at every step of its life (dispose reads the reuse identifier, a restart reads the wait strategies), so a change made afterwards would apply to part of the container only.</summary>
internal static class DefinitionReadOnly
{
    public static void ThrowIf(bool isReadOnly)
    {
        if (isReadOnly)
            throw new InvalidOperationException("The definition cannot be changed once a container or a volume is created from it. Change the definition before calling CreateContainer or CreateVolume, or copy it with its copy constructor.");
    }
}
