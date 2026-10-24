// smol-toml: TOML 1.0 parsing (tables, arrays of tables, dates, inline tables, errors) and stringification
import { parse, stringify, TomlDate } from "smol-toml";

const source = `
title = "TOML \\"example\\" \\u00e9"
literal = 'C:\\Users\\path'
multiline = """
Roses are red
  Violets are blue"""
integers = [ +99, 42, 0, -17, 1_000, 0xDEADBEEF, 0o755, 0b1101 ]
floats = [ 3.1415, -0.01, 5e+22, 1e06, -2E-2, 6.626e-34, inf, -inf, nan ]
dates = [ 1979-05-27T07:32:00Z, 1979-05-27T00:32:00.999999-07:00, 1979-05-27T07:32:00, 1979-05-27, 07:32:00 ]
inline = { x = 1, y = { z = [1, 2] } }
"quoted key" = true
dotted.key.path = "v"

[owner]
name = "Tom"

[database]
enabled = true
ports = [ 8000, 8001, 8002 ]
temp_targets = { cpu = 79.5, case = 72.0 }

[servers.alpha]
ip = "10.0.0.1"

[[products]]
name = "Hammer"
sku = 738594937

[[products]]

[[products]]
name = "Nail"
color = "gray"
`;

const replacer = (key, value) => typeof value === "bigint" ? value.toString() + "n" : typeof value === "number" && !Number.isFinite(value) ? String(value) : value;

export default () => {
  const results = [];
  const value = parse(source);
  results.push(JSON.stringify(value, replacer, 2));
  results.push(String(value.dates[0] instanceof TomlDate) + " " + value.dates.map(date => date.toISOString()).join(" "));
  results.push(stringify(value));
  results.push(JSON.stringify(parse("big = 9223372036854775807", { integersAsBigInt: true }), replacer));
  for (const invalid of ["a = ", "a = 1\na = 2", "[t]\n[t]", "a = 1979-13-01", "a = \"unterminated", "a = [1, \"mixed\"]x"]) {
    try {
      parse(invalid);
      results.push("no error");
    }
    catch (error) {
      results.push(error.name + " " + error.line + ":" + error.column + " " + error.message.split("\n")[0]);
    }
  }

  return results.join("\n----\n");
};
