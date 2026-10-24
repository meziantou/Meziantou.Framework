// Ajv: JSON Schema draft 2020-12 and draft-07 validators compiled to JavaScript functions (new Function), references,
// formats written as functions, coercion, defaults, removal of additional properties, errors and standalone code
import Ajv from "ajv";
import Ajv2020 from "ajv/dist/2020.js";
import standaloneCode from "ajv/dist/standalone/index.js";

const schema = {
  $id: "https://example.com/person.schema.json",
  type: "object",
  properties: {
    name: { type: "string", minLength: 1, maxLength: 50, pattern: "^[A-Z]" },
    age: { type: "integer", minimum: 0, exclusiveMaximum: 150 },
    email: { type: "string", format: "email" },
    tags: { type: "array", items: { type: "string" }, uniqueItems: true, maxItems: 3 },
    address: { $ref: "#/$defs/address" },
    kind: { enum: ["a", "b"] },
    settings: { type: "object", additionalProperties: { type: "boolean" }, propertyNames: { pattern: "^[a-z]+$" } },
    role: { oneOf: [{ const: "admin" }, { type: "string", pattern: "^user-" }] },
    coordinates: { type: "array", prefixItems: [{ type: "number" }, { type: "number" }], items: false },
  },
  required: ["name", "age"],
  if: { properties: { kind: { const: "b" } } },
  then: { required: ["email"] },
  dependentRequired: { email: ["name"] },
  $defs: {
    address: { type: "object", properties: { street: { type: "string" }, zip: { type: "string", pattern: "^\\d{5}$" } }, required: ["street"], additionalProperties: false },
  },
};

const inputs = [
  { name: "Alice", age: 30, email: "alice@example.com", tags: ["a", "b"], address: { street: "Main", zip: "12345" }, kind: "b", role: "admin", coordinates: [1, 2] },
  { name: "bob", age: 200, email: "not-an-email", tags: ["a", "a", "b", "c"], address: { zip: "1", extra: 1 }, kind: "c", settings: { Bad: "x" }, role: "guest", coordinates: [1, 2, 3] },
  { name: "Carol", age: 5.5, kind: "b" },
  "not an object",
];

export default () => {
  const results = [];
  const ajv = new Ajv2020({ allErrors: true, strict: false, code: { source: true } });
  ajv.addFormat("email", /^[^\s@]+@[^\s@]+\.[^\s@]+$/);
  const validate = ajv.compile(schema);
  for (const input of inputs) {
    const valid = validate(input);
    results.push(valid ? "valid" : JSON.stringify(validate.errors.map(error => [error.instancePath, error.keyword, error.message, error.params])));
  }

  const coercing = new Ajv({ coerceTypes: "array", useDefaults: true, removeAdditional: "all" });
  const coerce = coercing.compile({ type: "object", properties: { n: { type: "number" }, b: { type: "boolean" }, list: { type: "array", items: { type: "integer" } }, d: { type: "string", default: "dflt" } }, additionalProperties: false });
  const data = { n: "42", b: "true", list: "7", extra: 1 };
  results.push(String(coerce(data)) + " " + JSON.stringify(data));

  ajv.addKeyword({ keyword: "even", type: "number", validate: (expected, value) => (value % 2 === 0) === expected, errors: false });
  const even = ajv.compile({ type: "number", even: true });
  results.push([2, 3].map(value => even(value)).join(","));

  const code = standaloneCode(new Ajv({ code: { source: true, esm: false } }), new Ajv({ code: { source: true } }).compile({ type: "object", properties: { x: { type: "integer" } }, required: ["x"] }));
  results.push(code.length > 100 ? "standalone code generated" : code);
  return results.join("\n");
};
