// highlight.js (the ES module build of @highlightjs/cdn-assets, with the common languages): highlighting with the
// grammars of many languages (large regular expressions) and auto-detection
import hljs from "@highlightjs/cdn-assets/es/highlight.js";

const samples = {
  javascript: "async function* gen(a = 1, ...rest) { const { x, y: [z] } = await import('m'); yield `t ${x?.y ?? z}`; } // c\nclass A extends B { #p = 1n; static { } }",
  typescript: "interface A<T extends object = {}> { readonly x?: T[]; } type U = keyof A<string> | `a${number}`; enum E { X = 1 }",
  python: "@decorator\ndef f(x: int, *args, **kwargs) -> list[int]:\n    \"\"\"Doc\"\"\"\n    return [i ** 2 for i in range(x) if i % 2 == 0]  # comment\nprint(f'{f(3)!r:>10}')",
  csharp: "public record Point(int X, int Y) { public static Point operator +(Point a, Point b) => new(a.X + b.X, a.Y + b.Y); }\nvar s = $\"{p.X,5:N2}\"; // c",
  rust: "fn main() -> Result<(), Box<dyn Error>> { let v: Vec<u8> = vec![1, 2]; match v.get(0) { Some(&x) if x > 0 => println!(\"{x}\"), _ => {} } Ok(()) }",
  sql: "SELECT u.id, COUNT(*) AS n FROM users u LEFT JOIN orders o ON o.user_id = u.id WHERE u.created_at > '2020-01-01' GROUP BY u.id HAVING n > 1;",
  css: "@media (min-width: 640px) { .a > .b:hover::after { content: '\\201C'; color: #fff !important; } }",
  xml: "<?xml version=\"1.0\"?><!DOCTYPE a><a x=\"1\"><!-- c --><![CDATA[ <b> ]]><b>text &amp; more</b></a>",
  json: "{\"a\": [1, 2.5e10, true, null], \"b\": {\"c\": \"d\\n\"}}",
  bash: "#!/bin/bash\nfor f in *.txt; do echo \"${f%.txt}\" | sed -e 's/a/b/g' > \"$f.out\"; done; [[ -z $X ]] && exit 1",
  yaml: "key: value\nlist:\n  - a\n  - b: c\nanchor: &x { y: 1 }\nref: *x\nmulti: |\n  text\n",
  go: "func (s *Server) Handle(ctx context.Context) error { ch := make(chan int, 3); go func() { ch <- 1 }(); select { case v := <-ch: return nil }; return fmt.Errorf(\"%d\", v) }",
};

export default () => {
  const results = [];
  for (const [language, code] of Object.entries(samples)) {
    const result = hljs.highlight(code, { language });
    results.push(language + " (relevance " + result.relevance + "):\n" + result.value);
  }

  for (const code of [samples.python, samples.rust, samples.sql, samples.xml]) {
    const detected = hljs.highlightAuto(code, ["javascript", "python", "rust", "sql", "xml", "css"]);
    results.push("auto: " + detected.language + " " + detected.relevance);
  }

  return results.join("\n\n");
};
