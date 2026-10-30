---
title: Continuous integration
description: Snapshots are never updated on a build server, in a continuous testing runner or by an LLM agent.
---

A snapshot that changes on a build server is a failure to report, not a file to update. Snapshots are therefore never
updated, and no merge tool is started, in these environments, whatever the update strategy, including [Overwrite](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.Overwrite) and a
strategy set with `SNAPSHOTTESTING_STRATEGY`:

- **A build server or another non-interactive environment**, detected by the variables the major build servers set:
  `CI`, `TF_BUILD`, `GITHUB_ACTION`, `GITLAB_CI`, `JENKINS_URL`, `TEAMCITY_VERSION`, `APPVEYOR`, `TRAVIS_BUILD_ID`,
  `GO_SERVER_URL`, and MyGet's `BuildRunner`. Containers (`DOTNET_RUNNING_IN_CONTAINER`) and WSL (`WSL_DISTRO_NAME`)
  are treated the same way, as no diff tool is expected to show up there.
- **A continuous testing runner**: NCrunch, ReSharper or Visual Studio Live Unit Testing.
- **An LLM agent**, such as Claude Code, Codex, GitHub Copilot or Cursor, detected by
  [`Meziantou.Framework.LLMContext`](https://www.nuget.org/packages/Meziantou.Framework.LLMContext). An agent that runs
  the tests sees the failure and the actual files, and a person decides whether to approve them.

The test fails as with [Disallow](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.Disallow), the actual files are written, and the failure message says which environment was
detected.

## Review the actual files of a CI run

The actual files are written on the build server too. Publish the `**/__snapshots__/*.actual.*` files as build artifacts
when a run fails: you can download them, review them, and copy them over the verified files of your branch.

## Allow updates in such an environment

To allow updates anyway, for instance in a dev container where you run the tests yourself, set the
`SNAPSHOTTESTING_AUTODETECT_CONTINUOUS_ENVIRONMENT` environment variable to `false` (or `0`, `no`, `off`), or set
[AutoDetectContinuousEnvironment](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.AutoDetectContinuousEnvironment) to `false`:

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Tests/SnapshotTests.cs;region=Snapshot_AutoDetectContinuousEnvironment;format=md-fenced-code-block;dedent=true -->
```csharp
var settings = SnapshotSettings.Default with
{
    AutoDetectContinuousEnvironment = false,
};
```
<!-- endref -->

The update strategy then applies as on a developer machine.
