namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Options for building an image from a Dockerfile. Pass them to <see cref="ImageSource.FromDockerfile(string, string?, DockerfileBuildOptions?)"/>, which copies them: later changes do not affect the image source.</summary>
public sealed class DockerfileBuildOptions
{
    /// <summary>Gets the build-time variables, the values of the <c>ARG</c> instructions of the Dockerfile (<c>--build-arg</c>).</summary>
    public IDictionary<string, string> BuildArguments { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the labels added to the built image.</summary>
    public IDictionary<string, string> Labels { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets or sets the stage of a multi-stage Dockerfile to build (<c>--target</c>). When <see langword="null"/>, the last stage is built.</summary>
    public string? Target { get; set; }

    /// <summary>Gets or sets the platform to build the image for, such as <c>linux/amd64</c> (<c>--platform</c>). When <see langword="null"/>, <see cref="ContainerDefinition.Platform"/> is used.</summary>
    public string? Platform { get; set; }

    /// <summary>Gets or sets a value indicating whether the build ignores the build cache (<c>--no-cache</c>).</summary>
    public bool NoCache { get; set; }

    /// <summary>Gets or sets a value indicating whether the build pulls a newer version of the base images, even when they are on the machine (<c>--pull</c>).</summary>
    public bool Pull { get; set; }

    /// <summary>Gets or sets the name the image is tagged with, such as <c>my-app:test</c>. When <see langword="null"/>, the image gets a random name and is removed with the container.</summary>
    /// <remarks>An image with a tag of your choosing belongs to you: it is neither removed with the container nor by <see cref="ContainerRuntime.CleanupAsync(CancellationToken)"/>. Every build replaces it, and the build cache of the runtime makes a build of unchanged sources fast without leaving a new image behind.</remarks>
    public string? Tag { get; set; }
}
