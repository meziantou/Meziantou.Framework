// JSON5: parsing (comments, unquoted keys, trailing commas, hexadecimal, Infinity, multi-line strings) and stringify
import JSON5 from "json5";

const source = `// comment
{
  unquoted: 'and you can quote me on that',
  singleQuotes: 'I can use "double quotes" here',
  lineBreaks: "Look, Mom! \\
No \\\\n's!",
  hexadecimal: 0xdecaf,
  leadingDecimalPoint: .8675309, andTrailing: 8675309.,
  positiveSign: +1,
  trailingComma: 'in objects', andIn: ['arrays',],
  "backwardsCompatible": "with JSON",
  infinity: [Infinity, -Infinity, NaN],
  unicode: '\\u00e9\\x41',
  $ident_ü: 1,
  /* block */ nested: { a: [1, 2, { b: null }] },
}`;

export default () => {
  const results = [];
  const value = JSON5.parse(source, (key, item) => typeof item === "number" && !Number.isFinite(item) ? String(item) : item);
  results.push(JSON.stringify(value, null, 2));
  results.push(JSON5.stringify(JSON5.parse(source), null, 2));
  results.push(JSON5.stringify({ a: "it's", b: [1, Infinity, NaN], "c-d": undefined, e: () => 1, f: { toJSON5: () => "custom" } }, { quote: "\"" }));
  results.push(JSON5.stringify({ a: [1, { b: 2 }] }, { space: "\t", replacer: ["a", "b"] }));
  for (const invalid of ["{a: 1,,}", "{a b}", "[1, 2", "'unterminated", "{a: 01}"]) {
    try {
      JSON5.parse(invalid);
      results.push("no error");
    }
    catch (error) {
      results.push(error.name + ": " + error.message);
    }
  }

  return results.join("\n----\n");
};
