// js-yaml 5: loading (schemas, anchors, merge keys, multi-documents, custom tags, errors), dumping and the events of
// the parser
import { load, loadAll, dump, defineSequenceTag, Schema, CORE_SCHEMA, YAML11_SCHEMA, JSON_SCHEMA, FAILSAFE_SCHEMA, parseEvents, mergeTag, realMapTag } from "js-yaml";

const document = `
%YAML 1.2
---
string: hello
quoted: "line\\nbreak \\u00e9"
single: 'it''s'
int: 42
octal: 0o17
hex: 0x1F
float: 6.02e+23
infinity: .inf
nan: .NaN
bool: [true, false, yes, no]
null_values: [~, null, ""]
date: 2001-12-14t21:59:43.10-05:00
literal: |
  line 1
    line 2
folded: >
  folded
  text
base: &base { a: 1, b: 2 }
merged:
  <<: *base
  b: 3
list:
  - name: x
    values: [1, 2]
  - name: y
    values: []
`;

const point = defineSequenceTag("!point", {
  create: () => [],
  addItem: (carrier, item) => { carrier.push(item); },
  finalize: carrier => ({ x: carrier[0], y: carrier[1] }),
  identify: data => data !== null && typeof data === "object" && Object.keys(data).join() === "x,y",
  represent: data => [data.x, data.y],
});
const schema = new Schema([...YAML11_SCHEMA.tags, point]);

export default () => {
  const results = [];
  const value = load(document, { schema: YAML11_SCHEMA });
  results.push(JSON.stringify(value, null, 2));
  results.push(JSON.stringify(load("a: 0o17\nb: yes\nc: 1_000\nd: 0x1F", { schema: CORE_SCHEMA })));
  results.push(JSON.stringify(load("a: 1\nb: true\nc: null", { schema: JSON_SCHEMA })));
  results.push(JSON.stringify(load("a: 1\nb: [x]", { schema: FAILSAFE_SCHEMA })));
  results.push(JSON.stringify(loadAll("--- 1\n--- [2]\n--- {a: 3}\n")));
  results.push(JSON.stringify(load("p: !point [1, 2]", { schema })));
  const map = load("? [complex, key]\n: value\nplain: 1\n", { schema: CORE_SCHEMA.withTags(mergeTag, realMapTag) });
  results.push(JSON.stringify([...map]));
  results.push(dump(value, { sortKeys: true, lineWidth: 40 }));
  results.push(JSON.stringify([...parseEvents("a: [1, &x b]\nc: *x\n")]));
  results.push(dump({ p: { x: 1, y: 2 }, s: "multi\nline", k: "yes", n: null, u: undefined, arr: [[1, 2], { a: [] }] }, { schema, flowLevel: 2, skipInvalid: true, noRefs: true }));
  for (const invalid of ["a: [1, 2", "a: b: c", "- a\nb: c", "{a: 1, a: 2}"]) {
    try {
      load(invalid);
      results.push("no error");
    }
    catch (error) {
      results.push(error.name + ": " + error.reason + " at " + error.mark?.line + ":" + error.mark?.column);
    }
  }

  return results.join("\n----\n");
};
