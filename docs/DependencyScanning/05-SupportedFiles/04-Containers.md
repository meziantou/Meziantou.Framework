---
title: Containers
description: The container images found in Dockerfiles, Docker Compose files and Kubernetes manifests, and the charts of Helm.
---

## Image names and versions

Images are reported as [DockerImage](cref:Meziantou.Framework.DependencyScanning.DependencyType.DockerImage), wherever they come from (Dockerfiles, Docker Compose, Kubernetes, GitHub Actions,
Azure Pipelines):

- The tag is the part after the last `:` that follows the last `/`, so `localhost:5000/image:1.0` is reported as
  `localhost:5000/image` version `1.0`.
- An image pinned by digest (`image@sha256:...` or `image:tag@sha256:...`) is reported with the digest as its version.
  This version is not updatable, and the digest and the tag are in [Dependency.Metadata](cref:Meziantou.Framework.DependencyScanning.Dependency.Metadata) under the `digest` and `tag`
  keys.
- A name or a version that contains a variable reference, such as `image: node:${NODE_VERSION}`, is reported with a
  location that is not updatable.

<!-- ref:../../../tests/Meziantou.Framework.DependencyScanning.Tests/DependencyScannerTests.cs;region=DependencyScanning_ScanContent;format=md-fenced-code-block;dedent=true -->
```csharp
var content = Encoding.UTF8.GetBytes("""
    FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
    FROM build AS publish
    FROM nginx:1.27@sha256:0000000000000000000000000000000000000000000000000000000000000000
    """);

// The file path chooses the scanners: this content is scanned as a Dockerfile
var dependencies = await DependencyScanner.ScanFileAsync("/repo", "/repo/Dockerfile", content, cancellationToken);

// "FROM build" is a build stage, not an image
Assert.Equal(["mcr.microsoft.com/dotnet/sdk", "nginx"], dependencies.Select(dependency => dependency.Name).Order(StringComparer.Ordinal));

var sdk = dependencies.Single(dependency => dependency.Name == "mcr.microsoft.com/dotnet/sdk");
Assert.Equal(DependencyType.DockerImage, sdk.Type);
Assert.Equal("10.0", sdk.Version);

// An image pinned by digest has the digest as its version, and the tag in its metadata
var nginx = dependencies.Single(dependency => dependency.Name == "nginx");
Assert.Equal("sha256:0000000000000000000000000000000000000000000000000000000000000000", nginx.Version);
Assert.Equal("1.27", nginx.Metadata["tag"]);

// There is no file to write
Assert.False(sdk.VersionLocation?.IsUpdatable);
```
<!-- endref -->

## Dockerfiles

[DockerfileDependencyScanner](cref:Meziantou.Framework.DependencyScanning.Scanners.DockerfileDependencyScanner) reads the images of `FROM`, `COPY --from`, `RUN --mount=...,from=` and `# syntax=`.

- Only references with a tag or a digest are reported (except `# syntax=`), so that build stage names are not mistaken
  for images.
- The bodies of here-documents (`RUN <<EOF`, `COPY <<-"EOF" /file`) are skipped.
- When the whole image or the whole tag of a `FROM` comes from an `ARG` declared with a literal default value before
  the first `FROM` (`ARG NODE_VERSION=20` then `FROM node:${NODE_VERSION}`), the default value is reported. Its location
  in the `ARG` instruction is updatable, unless several `FROM` instructions use the same argument. Other variable
  references are reported with a location that is not updatable.

## Docker Compose

[DockerComposeDependencyScanner](cref:Meziantou.Framework.DependencyScanning.Scanners.DockerComposeDependencyScanner) reads `services.<name>.image` in `compose.yaml`, `compose.yml`,
`docker-compose.yaml`, `docker-compose.yml`, and their `compose.*.yaml` and `docker-compose.*.yml` variants. The image
of a service that has a `build` section is the name given to the image built from source, so it is not reported.

## Kubernetes

[KubernetesDependencyScanner](cref:Meziantou.Framework.DependencyScanning.Scanners.KubernetesDependencyScanner) reads the images of `containers`, `initContainers` and `ephemeralContainers` in the YAML
manifests of `Pod`, `PodTemplate`, `Deployment`, `StatefulSet`, `DaemonSet`, `ReplicaSet`, `ReplicationController`,
`Job` and `CronJob` objects, including `List` objects and files with several documents.

- Custom resources are not scanned.
- Files that contain `{{`, such as Helm chart templates, are templates rather than manifests: they are not scanned.

## Helm charts

[HelmChartDependencyScanner](cref:Meziantou.Framework.DependencyScanning.Scanners.HelmChartDependencyScanner) reads `Chart.yaml`, and `requirements.yaml` for apiVersion v1 charts, and reports their
dependencies as [HelmChart](cref:Meziantou.Framework.DependencyScanning.DependencyType.HelmChart). [HelmChartLockFileDependencyScanner](cref:Meziantou.Framework.DependencyScanning.Scanners.HelmChartLockFileDependencyScanner) reads `Chart.lock` and `requirements.lock`.

- The name is the chart `name`. The `repository` and `alias` values are in [Dependency.Metadata](cref:Meziantou.Framework.DependencyScanning.Dependency.Metadata) under the `repository`
  and `alias` keys.
- The entries of the lock files are not updatable, as they record a digest of the dependencies.
