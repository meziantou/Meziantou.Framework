// acorn, acorn-walk and astring: parsing modern JavaScript to an ESTree AST (with locations and comments), walking the
// tree, transforming it and generating the code back
import { parse, tokenizer } from "acorn";
import { simple, ancestor, full, findNodeAt } from "acorn-walk";
import { generate } from "astring";

const source = `// A module
import { a as b, default as c } from "./module.js";
export const answer = 6 * 7;
export default class Queue extends Base {
  #items = [];
  static #count = 0n;
  static { Queue.#count++; }
  constructor(...values) { super(); this.#items.push(...values); }
  get size() { return this.#items.length; }
  *[Symbol.iterator]() { yield* this.#items; }
  async *drain() { for await (const item of this) yield item; }
  has(value) { return #items in this && this.#items.includes(value); }
}
const { x = 1, y: [z, ...rest] = [], ...others } = input ?? {};
label: for (let i = 0; i < 10; i++) { if (i % 2) continue label; else break; }
const template = tag\`a\${b}c\`, regexp = /(?<year>\\d{4})-\\d{2}/v, big = 1_000_000n, maybe = a?.b?.[c]?.(d);
try { await import("./lazy.js"); } catch { } finally { }
x ||= 1; y &&= 2; z ??= 3;
using resource = getResource();
`;

export default () => {
  const comments = [];
  const ast = parse(source, { ecmaVersion: "latest", sourceType: "module", locations: true, onComment: comments });
  const results = [];
  const counts = {};
  full(ast, node => { counts[node.type] = (counts[node.type] ?? 0) + 1; });
  results.push(JSON.stringify(Object.fromEntries(Object.entries(counts).sort(([a], [b]) => a < b ? -1 : 1))));

  const identifiers = [];
  simple(ast, { Identifier: node => identifiers.push(node.name + "@" + node.loc.start.line + ":" + node.loc.start.column), PrivateIdentifier: node => identifiers.push("#" + node.name) });
  results.push(identifiers.join(" "));

  const nested = [];
  ancestor(ast, { ReturnStatement: (node, state, ancestors) => nested.push(ancestors.map(item => item.type).join(">")) });
  results.push(nested.join("\n"), JSON.stringify(comments.map(comment => [comment.type, comment.value])), findNodeAt(ast, null, null, "ClassDeclaration")?.node.id.name);

  // Renames b to renamed, and replaces the numeric literals by their double
  simple(ast, {
    Identifier: node => { if (node.name === "b") node.name = "renamed"; },
    Literal: node => { if (typeof node.value === "number") { node.value *= 2; node.raw = String(node.value); } },
  });
  results.push(generate(ast, { comments: false, indent: "  " }));
  results.push([...tokenizer("a = b + 1 /* c */ ? `t${d}` : /re/g", { ecmaVersion: "latest" })].map(token => token.type.label + (token.value === undefined ? "" : ":" + String(token.value))).join(" "));
  for (const invalid of ["let let = 1;", "class { }", "a ?. 1;", "function f() { 'use strict'; with (a) {} }", "const x;", "#x in y"]) {
    try {
      parse(invalid, { ecmaVersion: "latest", sourceType: "module" });
      results.push("no error");
    }
    catch (error) {
      results.push(error.name + ": " + error.message);
    }
  }

  return results.join("\n----\n");
};
