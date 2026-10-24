// Prism: prism.js is a script that defines the global Prism object (with the markup, CSS, C-like and JavaScript
// grammars), and each component is a script that adds a grammar to it
import "prismjs";
import "prismjs/components/prism-typescript.js";
import "prismjs/components/prism-jsx.js";
import "prismjs/components/prism-python.js";
import "prismjs/components/prism-rust.js";
import "prismjs/components/prism-csharp.js";
import "prismjs/components/prism-markdown.js";
import "prismjs/components/prism-json.js";
import "prismjs/components/prism-diff.js";

const Prism = globalThis.Prism;

const samples = {
  javascript: "const re = /ab+c/gi; let s = `x${1 + 2}`; class A { #x = 1; get y() { return this.#x; } }",
  typescript: "function f<T extends keyof U, U>(a: T): Promise<U[T]> { return null!; }",
  jsx: "const el = <Button onClick={() => setCount(c => c + 1)} disabled>Count: {count}</Button>;",
  python: "async def main():\n    async with lock:\n        return {k: v for k, v in items.items()}",
  rust: "impl<'a> Iterator for Parser<'a> { type Item = Token; fn next(&mut self) -> Option<Self::Item> { None } }",
  csharp: "[Obsolete] public sealed partial class C<T> where T : struct { private readonly int _x = 0x1F; }",
  markdown: "# Title\n\n* item with `code` and **bold**\n\n```js\nvar a;\n```",
  json: "{ \"a\": [1, 2, { \"b\": null }], \"c\": true }",
  diff: "--- a\n+++ b\n@@ -1,2 +1,2 @@\n-old\n+new\n context",
};

export default () => {
  const results = [];
  for (const [language, code] of Object.entries(samples)) {
    results.push(language + ":\n" + Prism.highlight(code, Prism.languages[language], language));
  }

  const tokens = Prism.tokenize("let x = 42; // answer", Prism.languages.javascript);
  results.push(JSON.stringify(tokens.map(token => typeof token === "string" ? token : [token.type, String(token.content)])));
  return results.join("\n\n");
};
