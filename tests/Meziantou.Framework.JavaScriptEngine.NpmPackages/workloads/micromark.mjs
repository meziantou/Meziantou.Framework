// micromark and the mdast/hast utilities (the parser and the syntax trees under remark and rehype, about a hundred ES
// modules): Markdown to HTML with GitHub Flavored Markdown, Markdown to mdast, mdast to hast and hast to HTML
import { micromark } from "micromark";
import { gfm, gfmHtml } from "micromark-extension-gfm";
import { fromMarkdown } from "mdast-util-from-markdown";
import { gfmFromMarkdown } from "mdast-util-gfm";
import { toHast } from "mdast-util-to-hast";
import { toHtml } from "hast-util-to-html";

const markdown = `# Release notes

Version **2.0** brings _many_ changes:

- [x] Faster parser
- [ ] Better errors
- Links: https://example.com and [docs][ref]

| Feature | Status |
| :------ | -----: |
| tables  | ✅ |
| ~~old~~ | removed |

Here is a footnote[^note].

\`\`\`js
console.log("hi");
\`\`\`

> **Note**
> Quoted <span>html</span> & entities &copy;.

[ref]: https://example.com/docs "Docs"
[^note]: The footnote.
`;

export default () => {
  const results = [];
  results.push(micromark(markdown, { extensions: [gfm()], htmlExtensions: [gfmHtml()] }));
  results.push(micromark(markdown, { allowDangerousHtml: true }));

  const tree = fromMarkdown(markdown, { extensions: [gfm()], mdastExtensions: [gfmFromMarkdown()] });
  const types = [];
  const visit = node => {
    types.push(node.type + (node.position ? "@" + node.position.start.line : ""));
    node.children?.forEach(visit);
  };
  visit(tree);
  results.push(types.join(" "));

  // Increases the depth of the headings, then converts the tree to HTML
  const raise = node => {
    if (node.type === "heading")
      node.depth = Math.min(node.depth + 1, 6);
    node.children?.forEach(raise);
  };
  raise(tree);
  results.push(toHtml(toHast(tree, { allowDangerousHtml: true }), { allowDangerousHtml: true }));
  results.push(toHtml({ type: "root", children: [{ type: "element", tagName: "p", properties: { className: ["a", "b"], dataX: 1, hidden: true }, children: [{ type: "text", value: "<&>" }] }] }, { quoteSmart: true, collapseEmptyAttributes: true }));
  return results.join("\n----\n");
};
