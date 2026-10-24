// Zod 4: schemas, parsing, refinements, transformations, discriminated unions, recursive types, error formatting and
// JSON Schema generation
import { z } from "zod";

const Address = z.object({ street: z.string().min(1), zip: z.string().regex(/^\d{5}$/), country: z.enum(["FR", "US"]).default("FR") });

const Shape = z.discriminatedUnion("kind", [
  z.object({ kind: z.literal("circle"), radius: z.number().positive() }),
  z.object({ kind: z.literal("rect"), width: z.number(), height: z.number() }),
]);

const Category = z.object({ name: z.string(), get children() { return z.array(Category).optional(); } });

const User = z.object({
  id: z.uuid(),
  name: z.string().trim().min(2).max(20),
  email: z.email(),
  age: z.coerce.number().int().min(0).optional(),
  tags: z.array(z.string()).max(3).default([]),
  address: Address,
  shape: Shape,
  createdAt: z.iso.datetime(),
  password: z.string(),
  confirm: z.string(),
  score: z.number().transform(value => Math.round(value * 100) / 100),
  meta: z.record(z.string(), z.union([z.string(), z.number(), z.boolean()])),
}).refine(user => user.password === user.confirm, { message: "Passwords do not match", path: ["confirm"] });

const valid = {
  id: "123e4567-e89b-42d3-a456-426614174000", name: "  Alice ", email: "alice@example.com", age: "30", address: { street: "Main", zip: "75001" },
  shape: { kind: "circle", radius: 2 }, createdAt: "2020-01-01T00:00:00Z", password: "x", confirm: "x", score: 1.23456, meta: { a: 1, b: "x", c: true },
};

const invalid = {
  id: "not-a-uuid", name: "A", email: "nope", age: "-1.5", tags: ["a", "b", "c", "d"], address: { street: "", zip: "123", country: "DE" },
  shape: { kind: "triangle" }, createdAt: "yesterday", password: "x", confirm: "y", score: "high", meta: { a: null },
};

export default () => {
  const results = [];
  results.push(JSON.stringify(User.parse(valid), null, 2));
  const failure = User.safeParse(invalid);
  results.push(JSON.stringify(failure.error.issues.map(issue => [issue.path.join("."), issue.code, issue.message])));
  results.push(z.prettifyError(failure.error));
  results.push(JSON.stringify(z.flattenError(User.safeParse({ ...valid, confirm: "z" }).error)));
  results.push(JSON.stringify(Category.parse({ name: "root", children: [{ name: "a", children: [{ name: "b" }] }] })));
  results.push(JSON.stringify(z.toJSONSchema(z.object({ a: z.string().min(1), b: z.array(z.number()).optional(), c: z.enum(["x", "y"]) })), null, 2));
  results.push(JSON.stringify(z.string().pipe(z.coerce.number()).safeParse("12.5")));
  return results.join("\n----\n");
};
