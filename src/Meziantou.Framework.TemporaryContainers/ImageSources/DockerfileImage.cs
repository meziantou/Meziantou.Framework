namespace Meziantou.Framework.TemporaryContainers;

/// <summary>An image built from a Dockerfile.</summary>
/// <param name="DockerfilePath">The path to the Dockerfile.</param>
/// <param name="ContextDirectory">The build context directory.</param>
/// <param name="Settings">The build options.</param>
internal sealed record DockerfileImage(string DockerfilePath, string ContextDirectory, DockerfileBuildSettings Settings) : ImageSource;
