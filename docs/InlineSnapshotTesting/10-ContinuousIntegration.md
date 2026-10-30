---
title: Continuous integration
description: Inline snapshots are never updated on a build server, in a continuous testing runner or by an LLM agent.
---

A snapshot that changes on a build server is a failure to report, not source code to rewrite. Inline snapshots are
therefore never updated, and no merge tool is started, in these environments, whatever the update strategy, including
`Overwrite` and a strategy set with `INLINESNAPSHOTTESTING_STRATEGY`:

- **A build server or another non-interactive environment**, detected by the variables the major build servers set
  (`CI`, `TF_BUILD`, `GITHUB_ACTION`, `GITLAB_CI`, `JENKINS_URL`, `TEAMCITY_VERSION`, ...). Containers and WSL are
  treated the same way, as no diff tool is expected to show up there.
- **A continuous testing runner**: NCrunch, ReSharper or Visual Studio Live Unit Testing.
- **An LLM agent**, such as Claude Code, Codex, GitHub Copilot or Cursor. An agent that runs the tests sees the failure
  and the difference, and does not get its source files rewritten behind its back.

The test fails as with `Disallow`, and the failure message says which environment was detected. The detection is the same
as for file snapshots; see [Continuous integration in SnapshotTesting](../SnapshotTesting/06-UpdatingSnapshots/05-ContinuousIntegration.md)
for the full list of variables.

## Allow updates in such an environment

To allow updates anyway, for instance in a dev container where you run the tests yourself, set the
`INLINESNAPSHOTTESTING_AUTODETECT_CONTINUOUS_ENVIRONMENT` environment variable to `false` (or `0`, `no`, `off`), or set
`AutoDetectContinuousEnvironment` to `false`:

<!-- ref:../../tests/Meziantou.Framework.InlineSnapshotTesting.Tests/InlineSnapshotTests.cs;region=InlineSnapshot_AutoDetectContinuousEnvironment;format=md-fenced-code-block;dedent=true -->
```csharp
InlineSnapshot
    .WithSettings(settings => settings.AutoDetectContinuousEnvironment = false)
    .Validate(new { Name = "meziantou" }, "Name: meziantou");
```
<!-- endref -->

The update strategy then applies as on a developer machine.
