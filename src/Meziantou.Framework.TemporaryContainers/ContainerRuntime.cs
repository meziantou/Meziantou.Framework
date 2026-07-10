namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Identifies the container runtime CLI used to manage containers.</summary>
public enum ContainerRuntime
{
    /// <summary>Automatically detect an available runtime (docker, then podman, then a platform-specific runtime).</summary>
    Auto,

    /// <summary>Use the <c>docker</c> CLI.</summary>
    Docker,

    /// <summary>Use the <c>podman</c> CLI.</summary>
    Podman,

    /// <summary>Use Apple's <c>container</c> CLI (macOS).</summary>
    AppleContainer,

    /// <summary>Use the WSL container CLI (<c>wslc</c>, Windows).</summary>
    Wslc,
}
