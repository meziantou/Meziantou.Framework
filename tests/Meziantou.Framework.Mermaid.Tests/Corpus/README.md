# Mermaid examples

The `.mmd` files are the examples of the documentation of Mermaid (the `mermaid-example` and `mermaid` code blocks
of `packages/mermaid/src/docs/syntax/<diagram>.md`), at commit `86340ba293ff605efe56f71fce943fcd408b8519` of
https://github.com/mermaid-js/mermaid, under the MIT license (`LICENSE.txt`). Each folder holds the examples of one
type of diagram, numbered in their order in the page. The tests check that every example parses, writes back to an
equivalent text, and draws.

The `gantt`, `timeline`, `mindmap`, `ishikawa`, `treeView`, `treemap` and `sankey` folders come from the same pages of
Mermaid 12.3.0. In `gantt/010.mmd`, the multi-line `themeCSS` value of the front matter, which is not valid YAML, is
removed.
