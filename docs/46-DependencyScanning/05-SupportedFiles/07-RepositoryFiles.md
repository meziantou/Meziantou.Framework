---
title: Repository files
description: The Git submodules, Renovate presets and agent plugins declared in the configuration files of a repository.
---

## Git submodules

`GitSubmoduleDependencyScanner` reads `.gitmodules`, and reports each submodule as `GitReference`:

- The name is the `url` as written in `.gitmodules`, and the version is the commit recorded in the Git index.
- When the index cannot be read, for example in a source archive or a clone made with `--no-checkout`, the submodule is
  reported with a `null` version.
- The submodule `name`, `path` and `branch` are in `Dependency.Metadata`, as well as the `url`, which is resolved
  against `remote.origin.url` when it is relative (`../other.git`), and is `null` when a relative URL cannot be resolved.

## Renovate configuration

`RenovateExtendsDependencyScanner` reads `renovate.json`, `renovate.json5`, `.renovaterc`, `.renovaterc.json`,
`.renovaterc.json5`, `renovaterc`, `renovaterc.json` and `renovaterc.json5`, at the root of the repository or in the
`.github` or `.gitlab` folder.

- The presets of `extends`, at the root and in `packageRules`, are reported as `RenovateConfiguration`.
- A preset written `name#ref` is reported with the ref as its version; a preset without `#` has no version.
- `.json5` files are read as JSON with comments and trailing commas. JSON5-only syntax, such as unquoted property names
  or single-quoted strings, is not supported.

## Agent plugins

`AgentPluginDependencyScanner` reads the plugin marketplaces of Claude Code and GitHub Copilot, and their settings files:

- **Marketplaces** (`.claude-plugin/marketplace.json`, `.github/plugin/marketplace.json`): the plugins hosted in a Git
  repository (`github`, `url` and `git-subdir` sources, and `owner/repo#ref` or Git URL strings) are reported as
  `AgentPlugin`, and the plugins published to npm as `Npm`. The version of a Git source is its `sha` when there is one,
  and its `ref` otherwise.
- **Settings** (`.claude/settings.json`, `.github/copilot/settings.json` and their `settings.local.json` counterparts):
  the Git and URL marketplaces declared in `extraKnownMarketplaces` are reported as `AgentPluginMarketplace`.
- The plugins stored next to the marketplace and the local marketplaces are not reported.
- The plugin or marketplace name, the `ref`, the `path` and the npm `registry` are in `Dependency.Metadata` under the
  `plugin`, `marketplace`, `ref`, `path` and `registry` keys.
