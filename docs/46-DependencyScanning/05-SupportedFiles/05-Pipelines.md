---
title: CI pipelines
description: The actions, images, tasks, templates and resources found in GitHub Actions workflows and Azure Pipelines.
---

## GitHub Actions

`GitHubActionsScanner` reads the workflows of `.github/workflows`, and the action metadata files (`action.yml`,
`action.yaml`) anywhere in the repository.

- `uses: owner/repo@ref` is reported as `GitHubActions`: the name is the part before `@`, and the version the ref.
- `uses: docker://image:tag`, the job `container` and `services`, and the `image` of a Docker container action are
  reported as `DockerImage`. See [Containers](04-Containers.md#image-names-and-versions).
- Local actions and reusable workflows (`uses: ./...`, and `uses: $/...`, which is resolved at the running commit) are
  not reported.

## GitHub Actions lock file

`GitHubActionsLockFileDependencyScanner` reads `.github/workflows/actions.lock`, the lock file written by
`gh actions-lock` (schema versions 0.0.1 to 0.0.3). It reports the entries of its `dependencies` section, which are the
actions used by the workflows and the actions these actions use:

- The key of an entry, `owner/repo@ref`, gives the name and the version. The version is the ref as written in `uses:`,
  so it can be a tag, a branch or a commit.
- The `commit`, `ref`, `hostname`, `owner_id` and `repo_id` values of the entry are in `Dependency.Metadata` under the
  `commit`, `ref`, `hostname`, `ownerId` and `repoId` keys. `commit` is the resolved commit as written (`sha1-...` or
  `sha256-...`), and `ref` is the tag or the branch this commit was resolved from.
- The entries are not updatable, as they record the commit and the identity of the repository.

## Azure Pipelines

`AzureDevOpsScanner` reads the `.yml` and `.yaml` files:

| Written as | Reported as |
| --- | --- |
| `pool.vmImage` | `AzureDevOpsVMPool`, with the image as the version |
| `task: Name@Version` | `AzureDevOpsTask` |
| `template: path@repository`, from steps, jobs, stages, `extends` and `variables` | `AzureDevOpsTemplate` |
| `git`, `github`, `githubenterprise` and `bitbucket` repository resources | `GitReference` |
| `npm` and `NuGet` package resources | `Npm`, `NuGet` |
| Container resources, and the job `container` and `services` | `DockerImage` |

- Deployment job lifecycle hooks are scanned too.
- The name of a package resource is reported as written (`<repository>/<package>` on GitHub Packages), and its
  `package` alias, `connection` and `type` are in `Dependency.Metadata`.
- A job `container` or `services` entry that names a container resource is not reported again. As the resource may be
  declared in another file, for example the pipeline that includes a template, a job `container` or `services` value is
  only reported when it is certainly an image: it has a tag, a digest or a `/`.
- Template expressions such as `${{ parameters.image }}` are not reported, nor are pipeline resources
  (`resources.pipelines`).
