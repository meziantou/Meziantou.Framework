using System.Text.Json.Serialization;

namespace Meziantou.Framework.TemporaryContainers.Internals;

[JsonSerializable(typeof(DockerApiModels.Version))]
[JsonSerializable(typeof(DockerApiModels.ErrorResponse))]
[JsonSerializable(typeof(DockerApiModels.CreateContainerResponse))]
[JsonSerializable(typeof(DockerApiModels.ContainerSummary[]))]
[JsonSerializable(typeof(DockerApiModels.PullProgress))]
[JsonSerializable(typeof(DockerApiModels.ExecCreateResponse))]
[JsonSerializable(typeof(DockerApiModels.ExecInspectResponse))]
[JsonSerializable(typeof(DockerInspectResult))]
[JsonSerializable(typeof(DockerApiModels.CreateContainerRequest))]
[JsonSerializable(typeof(DockerApiModels.ExecCreateRequest))]
[JsonSerializable(typeof(DockerApiModels.ExecStartRequest))]
[JsonSerializable(typeof(DockerApiModels.AuthConfigFile))]
[JsonSerializable(typeof(DockerApiModels.CredentialHelperGetResponse))]
[JsonSerializable(typeof(DockerApiModels.RegistryAuthHeader))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal sealed partial class DockerApiJsonContext : JsonSerializerContext;
