// Valibot: modular schemas, pipelines of actions, transformations, unions and issues
import * as v from "valibot";

const Schema = v.object({
  name: v.pipe(v.string(), v.trim(), v.minLength(2), v.maxLength(10)),
  email: v.pipe(v.string(), v.email()),
  age: v.optional(v.pipe(v.number(), v.integer(), v.minValue(0)), 18),
  website: v.optional(v.pipe(v.string(), v.url())),
  tags: v.pipe(v.array(v.string()), v.maxLength(2)),
  role: v.picklist(["admin", "user"]),
  id: v.union([v.pipe(v.string(), v.uuid()), v.number()]),
  price: v.pipe(v.string(), v.decimal(), v.transform(Number)),
  nested: v.variant("type", [v.object({ type: v.literal("a"), a: v.string() }), v.object({ type: v.literal("b"), b: v.number() })]),
  date: v.pipe(v.string(), v.isoDate()),
});

export default () => {
  const results = [];
  results.push(JSON.stringify(v.parse(Schema, { name: "  Bob  ", email: "bob@example.com", tags: ["x"], role: "user", id: 3, price: "12.50", nested: { type: "b", b: 1 }, date: "2020-02-29" }), null, 2));
  const failure = v.safeParse(Schema, { name: "B", email: "bob", age: -1.5, website: "nope", tags: ["a", "b", "c"], role: "guest", id: "x", price: "1.2.3", nested: { type: "c" }, date: "2020-02-30" });
  results.push(JSON.stringify(failure.issues.map(issue => [v.getDotPath(issue), issue.kind, issue.type, issue.message])));
  results.push(JSON.stringify(v.flatten(failure.issues)));
  results.push(JSON.stringify(v.safeParse(v.pipe(v.string(), v.toUpperCase(), v.regex(/^A/)), "abc")));
  return results.join("\n----\n");
};
