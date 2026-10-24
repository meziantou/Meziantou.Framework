// Prism: the tokenizer and the grammars that the components add to the global Prism object
import Prism from "prismjs";
import loadLanguages from "prismjs/components/index.js";

loadLanguages(["typescript", "jsx", "python", "rust", "csharp", "markdown", "json", "diff"]);

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
