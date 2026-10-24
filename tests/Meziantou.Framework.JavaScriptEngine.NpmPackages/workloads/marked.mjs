// marked: the lexer, the parser, GitHub Flavored Markdown, extensions and renderer overrides
import { Marked, marked } from "marked";

const markdown = `# Title with *emphasis* and \`code\`

Some **bold**, _italic_, ~~strike~~ and a [link](https://example.com "title") and <https://autolink.example>.

- item 1
- item 2
  1. nested
  2. list
- [x] task done
- [ ] task todo

> A quote
> > nested quote

\`\`\`js
const a = 1 < 2 && "<b>";
\`\`\`

| Left | Center | Right |
|:-----|:------:|------:|
| a    | b      | c     |
| \\| pipe | **x** | 1 |

Line with trailing spaces${"  "}
next line. HTML <span class="x">inline</span> &amp; entities &copy;.

    indented code

***

Term[^1].

[^1]: Footnote text.
`;

export default () => {
  const custom = new Marked({
    gfm: true,
    breaks: true,
    renderer: {
      heading({ tokens, depth }) {
        return `<h${depth} id="${this.parser.parseInline(tokens).toLowerCase().replace(/[^a-z]+/g, "-")}">${this.parser.parseInline(tokens)}</h${depth}>\n`;
      },
    },
    extensions: [{
      name: "mention",
      level: "inline",
      start: src => src.indexOf("@"),
      tokenizer(src) {
        const match = /^@(\w+)/.exec(src);
        return match ? { type: "mention", raw: match[0], user: match[1] } : undefined;
      },
      renderer: token => `<a class="mention" href="/u/${token.user}">@${token.user}</a>`,
    }],
  });

  return [
    marked.parse(markdown),
    JSON.stringify(marked.lexer("# A\n\n- b\n- c\n\n`d`").map(token => [token.type, token.raw])),
    custom.parse("# Hello World\n\nping @alice and @bob\nnew line"),
    marked.parseInline("**inline** _only_"),
  ].join("\n----\n");
};
