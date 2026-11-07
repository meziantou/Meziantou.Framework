// markdown-it: CommonMark rendering, the token stream, presets, options and plugins written against its API
import MarkdownIt from "markdown-it";

const markdown = `# Heading

Paragraph with *emphasis*, **strong**, \`code\`, a [link](http://example.com) and an ![image](a.png "t").

1. one
2. two
   - nested *item*

> quote with "quotes" -- and (c) (tm) ...

\`\`\`python
def f(): return 1
\`\`\`

| a | b |
|---|---|
| 1 | 2 |

<div>raw html</div>

www.example.com and https://example.org/path?x=1
`;

function containerPlugin(md) {
  md.block.ruler.before("fence", "note", (state, startLine, endLine, silent) => {
    const start = state.bMarks[startLine] + state.tShift[startLine];
    if (state.src.slice(start, state.eMarks[startLine]) !== ":::note")
      return false;
    if (silent)
      return true;

    let line = startLine + 1;
    while (line < endLine && state.src.slice(state.bMarks[line] + state.tShift[line], state.eMarks[line]) !== ":::")
      line++;

    state.push("note_open", "aside", 1).attrSet("class", "note");
    state.md.block.tokenize(state, startLine + 1, line);
    state.push("note_close", "aside", -1);
    state.line = line + 1;
    return true;
  });
}

export default () => {
  const commonmark = new MarkdownIt("commonmark");
  const full = new MarkdownIt({ html: true, linkify: true, typographer: true, highlight: (code, lang) => `<pre class="lang-${lang}"><code>${code.toUpperCase()}</code></pre>` });
  full.use(containerPlugin);
  const tokens = commonmark.parse("# A\n\n*b* [c](d)", {});
  return [
    commonmark.render(markdown),
    full.render(markdown + "\n:::note\nInside **note**\n:::\n"),
    JSON.stringify(tokens.map(token => [token.type, token.tag, token.nesting, token.children?.map(child => child.type)])),
    full.renderInline("*inline* -- text"),
  ].join("\n----\n");
};
