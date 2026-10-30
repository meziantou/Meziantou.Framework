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

## Private registries

The runtimes that drive a command line (`docker`, `podman`, `container`, `wslc`) use the credentials that command line
already has: log in with it once. The Docker Engine API runtime reads the credentials of the docker CLI, including its
credential helpers, from the `config.json` file of the directory in the `DOCKER_CONFIG` environment variable, or of
`~/.docker` when it is not set.
