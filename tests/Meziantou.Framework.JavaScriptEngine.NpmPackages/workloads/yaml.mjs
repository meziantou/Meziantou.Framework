// yaml (eemeli/yaml, its browser build): parsing to documents and to a CST, YAML 1.1 and 1.2, comments that survive
// edits, custom tags, errors and stringification
import YAML, { parseDocument, Document, isMap, visit, Parser, Lexer } from "yaml";

const source = `# A comment
name: example   # inline comment
version: 1.2.0
anchors:
  base: &base { a: 1 }
  copy: *base
  merged: { <<: *base, b: 2 }
sequence:
  - one
  - "two"
  - ? complex
    : key
binary: !!binary |
  aGVsbG8=
timestamp: 2001-12-14 21:59:43.10 -5
set: !!set { a, b }
multiline: >-
  folded
  text
`;

export default () => {
  const results = [];
  const document = parseDocument(source, { merge: true, version: "1.1" });
  results.push(JSON.stringify(document.toJS({ mapAsMap: false }), (key, value) => value instanceof Uint8Array ? Array.from(value) : value instanceof Set ? [...value] : value, 2));
  document.set("version", "2.0.0");
  document.setIn(["sequence", 0], "uno");
  document.addIn(["sequence"], { added: true });
  document.deleteIn(["anchors", "copy"]);
  results.push(String(document));

  const scalars = [];
  // The name of the time zone in Date.prototype.toString() is implementation-defined
  visit(document, { Scalar: (key, node) => { scalars.push(node.value instanceof Date ? node.value.toISOString() : String(node.value)); } });
  results.push(scalars.join(","));

  const created = new Document({ list: [1, 2, { nested: "x" }], text: "a\nb", empty: null });
  created.commentBefore = " generated";
  created.contents.items[0].value.flow = true;
  results.push(created.toString({ indent: 4, lineWidth: 30 }));
  results.push(YAML.stringify({ a: [1, { b: 2 }], c: "true", d: "1e3", e: new Map([[1, "x"]]) }, { defaultStringType: "QUOTE_DOUBLE", defaultKeyType: "PLAIN" }));

  const tokens = [...new Lexer().lex("a: [1, 2]\n")].map(token => JSON.stringify(token));
  results.push(tokens.join(" "));
  results.push(JSON.stringify([...new Parser().parse("- x\n- y: z\n")].map(token => token.type)));

  for (const invalid of ["a: [1, 2", "a: b: c", "key: value\n  bad indent: 1", "- a\n- b\nc: d"]) {
    const errors = parseDocument(invalid).errors;
    results.push(errors.map(error => error.code + " " + error.message.split("\n")[0]).join(" | "));
  }

  results.push(String(isMap(document.contents)));
  return results.join("\n----\n");
};
