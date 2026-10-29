---
title: Writing tests
description: Validate values and files, choose the snapshot format, name the snapshot files and make snapshots deterministic.
---

A snapshot test is a regular test that ends with `Snapshot.Validate`. These pages describe what you pass to it, where
the snapshots are stored and how to keep them stable from one run to the next.

- [Snapshot types](02-SnapshotTypes.md): store a value as text, JSON, SVG, PNG or any other format, and how each format
  is compared.
- [Snapshot files](03-SnapshotFiles.md): where the verified and actual files are stored, how their names are built, and
  how to store them in git and keep them out of the build.
- [Naming snapshots](04-NamingSnapshots.md): several assertions in one test, parameterized tests, naming strategies and
  the test context.
- [Helper methods](05-HelperMethods.md): wrap `Snapshot.Validate` in your own assertion methods.
- [Deterministic snapshots](06-DeterministicSnapshots.md): scrub GUIDs, dates, user and machine names, and control line
  endings and invisible characters.
