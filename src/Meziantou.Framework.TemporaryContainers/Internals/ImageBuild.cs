namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>What every runtime does the same way to build an image from a Dockerfile.</summary>
internal static class ImageBuild
{
    /// <summary>The name the built image is tagged with: the one of the options, or a random one the library owns.</summary>
    public static string GetTag(DockerfileImage image)
        => image.Settings.Tag ?? ResourceNaming.BuiltImagePrefix + Guid.NewGuid().ToString("N") + ":latest";

    /// <summary>The labels of the built image.</summary>
    /// <param name="definition">The definition of the container the image is built for.</param>
    /// <param name="image">The image source.</param>
    /// <param name="includeLibraryLabels">Whether the runtime labels the images it builds, which is how the cleanup and the reaper find them.</param>
    public static Dictionary<string, string> GetLabels(ContainerDefinition definition, DockerfileImage image, bool includeLibraryLabels)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in image.Settings.Labels)
            labels[name] = value;

        // An image with a tag of the caller's choosing belongs to the caller, so the cleanup must not find it. The
        // library labels also hold the time and the process of the build, which would make every build a new image.
        if (includeLibraryLabels && image.Settings.Tag is null)
        {
            foreach (var (name, value) in ResourceLabels.BuildForImage(definition))
                labels[name] = value;
        }

        return labels;
    }

    /// <summary>Builds the arguments of the <c>build</c> command of the docker, podman, wslc and Apple container CLIs, which share these options.</summary>
    public static List<string> BuildCliArguments(ContainerDefinition definition, DockerfileImage image, string tag, bool includeLibraryLabels)
    {
        var settings = image.Settings;
        var args = new List<string> { "build", "-t", tag, "-f", image.DockerfilePath };
        foreach (var (name, value) in settings.BuildArguments)
        {
            args.Add("--build-arg");
            args.Add(name + "=" + value);
        }

        foreach (var (name, value) in GetLabels(definition, image, includeLibraryLabels))
        {
            args.Add("--label");
            args.Add(name + "=" + value);
        }

        if (settings.Target is not null)
        {
            args.Add("--target");
            args.Add(settings.Target);
        }

        if (settings.GetPlatform(definition) is { } platform)
        {
            args.Add("--platform");
            args.Add(platform);
        }

        if (settings.NoCache)
            args.Add("--no-cache");

        if (settings.Pull)
            args.Add("--pull");

        args.Add(image.ContextDirectory);
        return args;
    }

    /// <summary>Creates the callback that writes the output of a build to the logger of the container, or <see langword="null"/> when there is no logger.</summary>
    public static Action<string>? CreateOutputCallback(ContainerDefinition definition, DockerfileImage image)
    {
        if (definition.Logging.Logger is not { } logger)
            return null;

        return line =>
        {
            if (!string.IsNullOrWhiteSpace(line))
                Log.ImageBuildOutput(logger, image.DockerfilePath, line.TrimEnd());
        };
    }
}
