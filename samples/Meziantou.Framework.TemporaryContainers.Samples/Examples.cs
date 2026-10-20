namespace Meziantou.Framework.TemporaryContainers.Samples;

/// <summary>Usage examples of the public API, shown by the XML documentation. They need a container runtime, so they are compiled in CI but not run.</summary>
internal static class Examples
{
    public static async Task CreateContainer()
    {
        #region ContainerDefinition_Example
        var definition = new ContainerDefinition(ImageSource.FromRegistry("redis:8"));
        definition.Environment.Add("TZ", "UTC");
        definition.Ports.Add(new ContainerPort(6379));
        definition.WaitStrategies.Add(Wait.ForPort(6379));

        await using var container = definition.CreateContainer();
        await container.StartAsync();
        #endregion
    }

    public static async Task CreateContainerWithVolume()
    {
        #region VolumeDefinition_Example
        await using var volume = new VolumeDefinition().CreateVolume();

        var definition = new ContainerDefinition(ImageSource.FromRegistry("redis:8"));
        definition.Mounts.AddVolume(volume, "/data");

        await using var container = definition.CreateContainer();
        await container.StartAsync();
        #endregion
    }
}
