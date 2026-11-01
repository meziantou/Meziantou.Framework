namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>The endpoints to try, and why none was found when the list is empty.</summary>
internal sealed record DockerEndpointDiscoveryResult(IReadOnlyList<DockerApiTransport.Endpoint> Endpoints, IReadOnlyList<string> Diagnostics);
