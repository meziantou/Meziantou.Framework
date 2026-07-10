namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Describes where the image used to create a container comes from.</summary>
public abstract record ImageSource
{
    public static implicit operator ImageSource(string name) => FromRegistry(name);


    /// <summary>Creates an image source from a registry reference (for example <c>redis:8</c>).</summary>
    /// <param name="name">The image name, optionally including a tag or digest.</param>
    public static ImageSource FromRegistry(string name)
    {
        return new RegistryImage(name);
    }

    /// <summary>Creates an image source from a Dockerfile and build context directory.</summary>
    /// <param name="dockerfilePath">The path to the Dockerfile.</param>
    /// <param name="contextDirectory">The build context directory.</param>
    public static ImageSource FromDockerfile(string dockerfilePath, string? contextDirectory = null)
    {
        var fullPath = Path.GetFullPath(dockerfilePath);
        if (contextDirectory is null)
        {
            contextDirectory = Path.GetDirectoryName(fullPath) ?? throw new ArgumentException($"Cannot determine the directory of the Dockerfile '{dockerfilePath}'.", nameof(dockerfilePath));
        }
        else
        {
            contextDirectory = Path.GetFullPath(contextDirectory);
        }

        return new DockerfileImage(fullPath, contextDirectory);
    }

    /// <summary>Creates an image source from a Docker image archive produced by <c>docker save</c>.</summary>
    /// <param name="archivePath">The path to the image archive.</param>
    public static ImageSource FromArchive(string archivePath)
    {
        return new ArchiveImage(Path.GetFullPath(archivePath));
    }

    /// <summary>Creates an image source from an image that already exists locally.</summary>
    /// <param name="imageId">The image id or name.</param>
    public static ImageSource FromExisting(string imageId)
    {
        return new ExistingImage(imageId);
    }
}
