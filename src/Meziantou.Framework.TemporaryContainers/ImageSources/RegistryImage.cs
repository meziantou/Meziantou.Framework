namespace Meziantou.Framework.TemporaryContainers;

/// <summary>An image referenced by its name in a registry (for example <c>redis:8</c>).</summary>
/// <param name="Name">The image name, optionally including a tag or digest.</param>
internal sealed record RegistryImage(string Name) : ImageSource;
