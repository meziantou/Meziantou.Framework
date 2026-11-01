namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>The network a container is created on.</summary>
/// <param name="Name">The network, or <see langword="null"/> for the default network of the runtime.</param>
/// <param name="Aliases">The aliases of the container on that network.</param>
internal readonly record struct NetworkEndpoint(string? Name, IReadOnlyList<string> Aliases);
