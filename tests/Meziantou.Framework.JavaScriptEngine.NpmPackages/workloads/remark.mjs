// unified with remark and rehype: Markdown to mdast, GitHub Flavored Markdown, transformations of the tree, mdast to
// hast and serialization (hundreds of small ES modules: micromark, mdast-util-*, hast-util-*...)
import { unified } from "unified";
import remarkParse from "remark-parse";
import remarkGfm from "remark-gfm";
import remarkRehype from "remark-rehype";
import rehypeStringify from "rehype-stringify";

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
> Quoted.

[ref]: https://example.com/docs "Docs"
[^note]: The footnote.
`;

// Increases the depth of the headings and collects the text of the links
function transform(collected) {
  return () => tree => {
    const visit = node => {
      if (node.type === "heading")
        node.depth = Math.min(node.depth + 1, 6);
      if (node.type === "link")
        collected.push(node.url);
      node.children?.forEach(visit);
    };
    visit(tree);
  };
}

export default async () => {
  const links = [];
  const html = await unified().use(remarkParse).use(remarkGfm).use(transform(links)).use(remarkRehype).use(rehypeStringify).process(markdown);
  const tree = unified().use(remarkParse).use(remarkGfm).parse(markdown);
  const types = [];
  const visit = node => {
    types.push(node.type);
    node.children?.forEach(visit);
  };
  visit(tree);
  return [String(html), links.join("\n"), types.join(" ")].join("\n----\n");
};
