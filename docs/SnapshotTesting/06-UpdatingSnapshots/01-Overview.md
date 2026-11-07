---
title: Updating snapshots
description: Review and approve new snapshots with the approval tool, an update strategy or a merge tool, and keep CI strict.
---

When a snapshot does not match, the test fails, the new content is written to an `.actual` file next to the verified
file, and the failure message lists the files that changed, appeared or disappeared. Review the difference, then either
fix the code or approve the new snapshot. There are three ways to approve:

- **Approve the actual files** after the run, with the approval tool or by renaming the files yourself. Nothing changes
  until you decide. See [The approval tool](03-ApprovalTool.md).
- **Let the test run overwrite the verified files**, with the [Overwrite](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.Overwrite) or [OverwriteWithoutFailure](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.OverwriteWithoutFailure) update strategy,
  and review the result in your version control diff. See [Update strategies](02-UpdateStrategies.md).
- **Merge each change in a merge tool** that the test run opens, with the [MergeTool](cref:Meziantou.Framework.SnapshotTesting.MergeTool) or [MergeToolSync](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.MergeToolSync) strategy. See
  [Merge tools](04-MergeTools.md).

The default strategy, [Disallow](cref:Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategy.Disallow), never changes a verified file: you approve with the tool. Whatever the strategy,
snapshots are never updated on a build server, in a continuous testing runner or when the tests are run by an LLM agent.
See [Continuous integration](05-ContinuousIntegration.md).

- [Update strategies](02-UpdateStrategies.md): what happens when a snapshot does not match, and how to choose a strategy
  for a run.
- [The approval tool](03-ApprovalTool.md): approve every actual file, or one by one, from the command line.
- [Merge tools](04-MergeTools.md): which tool is started, and how to choose it.
- [Continuous integration](05-ContinuousIntegration.md): the environments where snapshots are never updated.
