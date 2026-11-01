---
title: Images
description: Run an image from a registry, a Dockerfile, an archive or the local machine, and choose when it is pulled.
---

The image of a container is an [ImageSource](cref:Meziantou.Framework.TemporaryContainers.ImageSource), passed to the [ContainerDefinition](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition) constructor or set with
`definition.Image`:

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_ImageSources;format=md-fenced-code-block;dedent=true -->
```csharp
var fromRegistry = new ContainerDefinition(ImageSource.FromRegistry("nginx:1.29")); // a name with a tag or a digest
var fromString = new ContainerDefinition("nginx:1.29"); // a string converts to a registry image
var fromDockerfile = new ContainerDefinition(ImageSource.FromDockerfile("src/MyApp/Dockerfile", contextDirectory: "src"));
var fromArchive = new ContainerDefinition(ImageSource.FromArchive("artifacts/my-app.tar")); // produced by 'docker save'
var fromExisting = new ContainerDefinition(ImageSource.FromExisting("my-app:dev")); // an image already on the machine
```
<!-- endref -->

| Factory method | The image comes from |
| --- | --- |
| `ImageSource.FromRegistry(name)` | A registry, by name with an optional tag or digest. A string converts to it implicitly. |
| `ImageSource.FromDockerfile(dockerfilePath, contextDirectory)` | A Dockerfile, built when the container is created. |
| `ImageSource.FromArchive(archivePath)` | An archive written by `docker save`, loaded when the container is created. The loaded image stays on the machine. |
| `ImageSource.FromExisting(imageId)` | An image already on the machine, by id or name. |

Pin a tag (`postgres:17`) or a digest rather than `latest`: a test that ran yesterday then runs against the same server
today.

## Build an image from a Dockerfile

Use a Dockerfile to test your own application, or an image with test data baked in:

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/ReadmeExamples.cs;region=TemporaryContainers_BuildImage;format=md-fenced-code-block;dedent=true -->
```csharp
var definition = new ContainerDefinition(ImageSource.FromDockerfile("./Dockerfile", "."));
definition.Ports.Add(new ContainerPort(8080));
definition.WaitStrategies.Add(Wait.ForLogMessage("SERVER READY"));

await using var container = definition.CreateContainer();
await container.StartAsync();
```
<!-- endref -->

- Relative paths are resolved against the current directory when you call [FromDockerfile](cref:Meziantou.Framework.TemporaryContainers.ImageSource.FromDockerfile). The context directory
  defaults to the directory of the Dockerfile.
- The image is built when the container is created. The files of the context keep their permissions, so a script stays
  executable in the image.
- The image is built for the container and removed with it, unless the container has a [ReuseId](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.ReuseId). `wslc` does not label
  the images it builds, so they are left behind there.
- The `.dockerignore` file of the context is applied by every runtime, the Docker Engine API runtime included: the
  files it excludes are not sent to the builder. A `<Dockerfile>.dockerignore` file next to the Dockerfile (for
  instance `Dockerfile.dockerignore`) takes precedence over it. The Dockerfile and the ignore file are always sent.
- The output of the build is written to [`definition.Logging.Logger`](../05-WorkingWithContainers.md) at the `Debug` level.

### Build options

Pass a [DockerfileBuildOptions](cref:Meziantou.Framework.TemporaryContainers.DockerfileBuildOptions) to set the build arguments, the stage to build, or the platform:

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_DockerfileBuildOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new DockerfileBuildOptions
{
    Target = "runtime",              // the stage of a multi-stage Dockerfile to build
    Platform = "linux/amd64",        // build for another platform than the machine
    Pull = true,                     // pull newer base images
    NoCache = false,                 // keep using the build cache
};
options.BuildArguments.Add("DOTNET_VERSION", "10.0"); // the value of 'ARG DOTNET_VERSION'
options.Labels.Add("org.opencontainers.image.title", "my-app");

var definition = new ContainerDefinition(ImageSource.FromDockerfile("src/MyApp/Dockerfile", "src", options));
```
<!-- endref -->

| Option | Docker CLI flag | Effect |
| --- | --- | --- |
| [BuildArguments](cref:Meziantou.Framework.TemporaryContainers.DockerfileBuildOptions.BuildArguments) | `--build-arg` | The values of the `ARG` instructions. They are hidden in the error messages, so they can carry a token. |
| [Target](cref:Meziantou.Framework.TemporaryContainers.DockerfileBuildOptions.Target) | `--target` | The stage of a multi-stage Dockerfile to build. The last stage when not set. |
| [Platform](cref:Meziantou.Framework.TemporaryContainers.DockerfileBuildOptions.Platform) | `--platform` | The platform to build for. [ContainerDefinition.Platform](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.Platform) when not set. |
| [Pull](cref:Meziantou.Framework.TemporaryContainers.DockerfileBuildOptions.Pull) | `--pull` | Pull newer versions of the base images, even when they are on the machine. |
| [NoCache](cref:Meziantou.Framework.TemporaryContainers.DockerfileBuildOptions.NoCache) | `--no-cache` | Build every step again, without the build cache. |
| [Labels](cref:Meziantou.Framework.TemporaryContainers.DockerfileBuildOptions.Labels) | `--label` | Labels added to the image. |
| [Tag](cref:Meziantou.Framework.TemporaryContainers.DockerfileBuildOptions.Tag) | `-t` | The name of the image. See below. |

The options are copied when you call [FromDockerfile](cref:Meziantou.Framework.TemporaryContainers.ImageSource.FromDockerfile): changing them afterwards does not change the image
source.

### Keep the image between runs

By default, every run builds an image with a random name and removes it with the container. Give the image a name of
your own to keep it:

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_DockerfileStableTag;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new DockerfileBuildOptions { Tag = "my-app:tests" };
var definition = new ContainerDefinition(ImageSource.FromDockerfile("./Dockerfile", ".", options));
```
<!-- endref -->

An image with a tag of your choosing belongs to you: it is neither removed with the container nor by the
[cleanup](../07-Cleanup.md). Every run still builds it, but the build cache of the runtime makes the build of unchanged
sources fast, and the new build replaces the image instead of leaving a new one behind.

## Choose when the image is pulled

[PullPolicy](cref:Meziantou.Framework.TemporaryContainers.PullPolicy) decides when a registry image is pulled:

| Value | Behavior |
| --- | --- |
| [PullPolicy.IfMissing](cref:Meziantou.Framework.TemporaryContainers.PullPolicy.IfMissing) (default) | Pull the image only when it is not on the machine. |
| [PullPolicy.Always](cref:Meziantou.Framework.TemporaryContainers.PullPolicy.Always) | Pull the image on every run, even when a local copy exists. |
| [PullPolicy.Never](cref:Meziantou.Framework.TemporaryContainers.PullPolicy.Never) | Never pull; fail when the image is not on the machine. |

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_PullPolicy;format=md-fenced-code-block;dedent=true -->
```csharp
definition.PullPolicy = PullPolicy.Always; // pull a moving tag such as 'latest' on every run
```
<!-- endref -->

`wslc` cannot honor [PullPolicy.Never](cref:Meziantou.Framework.TemporaryContainers.PullPolicy.Never).

## Choose the platform

Some images have no variant for the architecture of the machine: `mcr.microsoft.com/mssql/server` is only published for
`linux/amd64`, so it fails on Apple Silicon with "no matching manifest". Set
[Platform](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.Platform) to pull and run the variant of another platform:

<!-- ref:../../../samples/Meziantou.Framework.TemporaryContainers.Samples/DocumentationExamples.cs;region=TemporaryContainers_Platform;format=md-fenced-code-block;dedent=true -->
```csharp
definition.Platform = "linux/amd64"; // run the amd64 variant, for instance on Apple Silicon
```
<!-- endref -->

- The platform is passed to the pull, to the creation of the container, and to the build of a Dockerfile (unless
  [DockerfileBuildOptions.Platform](cref:Meziantou.Framework.TemporaryContainers.DockerfileBuildOptions.Platform) sets another one).
- Running an image of another architecture needs an emulator: Rosetta on Apple Silicon (Docker Desktop and Apple's
  `container`), or QEMU on Linux.
- A container adopted through [ReuseId](cref:Meziantou.Framework.TemporaryContainers.ContainerDefinition.ReuseId) must have been created for the same platform.

## Private registries

The runtimes that drive a command line (`docker`, `podman`, `container`, `wslc`) use the credentials that command line
already has: log in with it once. The Docker Engine API runtime reads the credentials of the docker CLI, including its
credential helpers, from the `config.json` file of the directory in the `DOCKER_CONFIG` environment variable, or of
`~/.docker` when it is not set. When it builds a Dockerfile, it sends the credentials of the registries of the `FROM`
and `COPY --from` images, so a base image can come from a private registry.
