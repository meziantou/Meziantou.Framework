// Shiki with its JavaScript regex engine: the Oniguruma patterns of the TextMate grammars are translated to JavaScript
// regular expressions (the v flag, lookbehinds, named groups, Unicode properties) and run by the tokenizer of VS Code
import { createHighlighterCore } from "shiki/core";
import { createJavaScriptRegexEngine } from "shiki/engine/javascript";

const samples = {
  typescript: "import { a, type B } from './m';\nexport default class C<T> implements I {\n  #x?: T;\n  constructor(private readonly y: number = 1) { super(); }\n  async *gen(): AsyncGenerator<string> { yield `v${this.y ?? 0}`; }\n}\n",
  python: "@dataclass(frozen=True)\nclass Point:\n    x: float\n    def dist(self, other: 'Point') -> float:\n        return ((self.x - other.x) ** 2) ** 0.5  # comment\n",
  css: ".btn:hover > span::before { content: \"\\201C\"; color: rgb(0 0 0 / 50%); transition: all .2s ease-in-out; }\n",
  html: "<!DOCTYPE html>\n<html lang=\"en\"><head><style>a{color:red}</style><script>let a = 1 < 2;</script></head><body><!-- c --><p class=\"x\">Hi &amp; bye</p></body></html>\n",
  json: "{\n  \"name\": \"x\",\n  \"version\": \"1.0.0\",\n  \"list\": [1, 2.5, true, null]\n}\n",
};

export default async () => {
  const highlighter = await createHighlighterCore({
    themes: [import("shiki/themes/github-dark.mjs"), import("shiki/themes/github-light.mjs")],
    langs: [import("shiki/langs/typescript.mjs"), import("shiki/langs/python.mjs"), import("shiki/langs/css.mjs"), import("shiki/langs/html.mjs"), import("shiki/langs/json.mjs")],
    engine: createJavaScriptRegexEngine(),
  });

  // The tokenizer stops a line after a time limit (500 ms by default): the result must not depend on the speed
  const options = { tokenizeTimeLimit: 0 };
  const results = [];
  for (const [lang, code] of Object.entries(samples)) {
    results.push(highlighter.codeToHtml(code, { lang, theme: "github-dark", ...options }));
  }

  results.push(highlighter.codeToHtml(samples.typescript, { lang: "typescript", themes: { light: "github-light", dark: "github-dark" }, ...options }));
  const tokens = highlighter.codeToTokensBase("const a = 1;", { lang: "typescript", theme: "github-light", includeExplanation: true, ...options });
  results.push(JSON.stringify(tokens[0].map(token => [token.content, token.color, token.explanation.map(item => item.scopes.map(scope => scope.scopeName).join(" "))])));
  return results.join("\n\n");
};
